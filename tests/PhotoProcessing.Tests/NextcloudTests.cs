using System.Net;
using PhotoProcessing.Core;
using PhotoProcessing.Core.Nextcloud;

namespace PhotoProcessing.Tests;

public class NextcloudTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static NextcloudClient Client(HttpMessageHandler handler) =>
        new("https://cloud.example.com", "sam", _ => ValueTask.FromResult("access-1"), handler);

    [Fact]
    public async Task List_parses_propfind_and_skips_the_folder_itself()
    {
        const string body = """
            <?xml version="1.0"?>
            <d:multistatus xmlns:d="DAV:">
              <d:response><d:href>/remote.php/dav/files/sam/Photos/Inbox/</d:href>
                <d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
              <d:response><d:href>/remote.php/dav/files/sam/Photos/Inbox/IMG%204899.CR2</d:href>
                <d:propstat><d:prop>
                  <d:getlastmodified>Fri, 03 Oct 2026 09:48:00 GMT</d:getlastmodified>
                  <d:getcontentlength>6782285</d:getcontentlength><d:resourcetype/><d:getetag>"abc123"</d:getetag>
                </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
            </d:multistatus>
            """;
        var handler = new FakeHandler(_ => new HttpResponseMessage((HttpStatusCode)207) { Content = new StringContent(body) });
        using var client = Client(handler);

        var items = await client.ListAsync("Photos/Inbox");

        var item = Assert.Single(items);
        Assert.Equal("Photos/Inbox/IMG 4899.CR2", item.Path);
        Assert.Equal("IMG 4899.CR2", item.Name);
        Assert.Equal(6782285, item.Size);
        Assert.Equal("abc123", item.ETag);
        Assert.False(item.IsFolder);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("PROPFIND", request.Method.Method);
        Assert.Equal("https://cloud.example.com/remote.php/dav/files/sam/Photos/Inbox/", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("access-1", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Missing_folder_lists_as_empty()
    {
        using var client = Client(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Empty(await client.ListAsync("Photos/Nope"));
    }

    [Fact]
    public async Task Move_never_overwrites_and_escapes_names()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        using var client = Client(handler);

        await client.MoveAsync("Photos/Inbox/IMG 1.CR2", "Photos/Archive/IMG 1.CR2");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("F", request.Headers.GetValues("Overwrite").Single());
        Assert.Equal("https://cloud.example.com/remote.php/dav/files/sam/Photos/Archive/IMG%201.CR2",
            request.Headers.GetValues("Destination").Single());
    }

    [Fact]
    public async Task Unique_path_appends_a_counter_before_all_extensions()
    {
        var taken = new HashSet<string> { "/remote.php/dav/files/sam/A/IMG_1.CR2.xmp", "/remote.php/dav/files/sam/A/IMG_1-2.CR2.xmp" };
        var handler = new FakeHandler(r => new HttpResponseMessage(
            taken.Contains(r.RequestUri!.AbsolutePath) ? (HttpStatusCode)207 : HttpStatusCode.NotFound));
        using var client = Client(handler);

        Assert.Equal("A/IMG_1-3.CR2.xmp", await client.UniquePathAsync("A/IMG_1.CR2.xmp"));
    }

    [Fact]
    public async Task Every_request_asks_for_a_current_token()
    {
        var issued = 0;
        var handler = new FakeHandler(_ => new HttpResponseMessage((HttpStatusCode)207));
        using var client = new NextcloudClient("https://cloud.example.com", "sam",
            _ => ValueTask.FromResult($"access-{++issued}"), handler);

        await client.ExistsAsync("A");
        await client.ExistsAsync("B");

        Assert.Equal(["access-1", "access-2"], handler.Requests.Select(r => r.Headers.Authorization!.Parameter));
    }
}
