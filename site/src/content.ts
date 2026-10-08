// What the page says, kept as data so the page and its WebMCP tools (components/webmcp.tsx) tell the
// same story. Text may use `code` and [links](url), which <Rich> renders and plainText() flattens.

export const repo = "https://github.com/sstrabe/Lighttable";
export const setupGuide = `${repo}#setup`;

export const summary = "Upload a raw photo to Nextcloud and get back a finished JPEG, edited by Claude in darktable.";
export const intro = "Drop a raw file in Nextcloud and a JPEG comes back a few minutes later. The edit comes with it as darktable history, so you can open the raw and take it further yourself.";

export type ExampleEdit = {
    id: string;
    before: string;
    after: string;
    alt: string;
    beforeLabel: string;
    instructions?: string;
    caption: string;
    modules: string[];
};

// Real edits: the camera's own JPEG against Claude's edit of the raw, with lines from its notes.md.
export const hero: ExampleEdit = {
    id: "forest",
    before: "/photos/forest-before.jpg",
    after: "/photos/forest-after.jpg",
    alt: "A forest valley in the mountains",
    beforeLabel: "Camera JPEG",
    caption: "No instructions. From Claude's notes: “The backlit conifers were nearly black and the sky was washed out.” Claude lifted the shadows to open the forest, pulled the highlights down to keep the clouds, and left some of the mountain haze in because it is part of the scene.",
    modules: ["exposure", "color calibration", "sigmoid", "tone equalizer", "color balance rgb", "local contrast"],
};

export const gallery: ExampleEdit[] = [
    {
        id: "contrail",
        before: "/photos/contrail-before.jpg",
        after: "/photos/contrail-after.jpg",
        alt: "A contrail over trees at dusk",
        beforeLabel: "Camera JPEG",
        caption: "No instructions. From Claude's notes: “The sky looked slightly grey and flat.” Claude added vibrance and contrast for a cleaner twilight blue and kept the as-shot white balance. It tried deeper blacks first, saw they erased the meadow, and backed off.",
        modules: ["exposure", "sigmoid", "color balance rgb"],
    },
    {
        id: "dusk",
        before: "/photos/dusk-before.jpg",
        after: "/photos/dusk-after.jpg",
        alt: "Dusk over the mountains",
        beforeLabel: "Camera JPEG",
        instructions: "make the sunset pop",
        caption: "Instructions: “make the sunset pop”. From Claude's notes: “The sky was slightly hazy and flat and the sunset glow looked washed out.” Claude warmed the white balance with a touch of magenta to keep the salmon-pink glow, and lifted the shadows gently to separate the ridges.",
        modules: ["exposure", "color calibration", "sigmoid", "color balance rgb", "local contrast", "tone equalizer"],
    },
];

export const steps: { title: string; text: string }[] = [
    { title: "Upload", text: "Put a raw file in `Photos/Processing/Inbox` on Nextcloud. CR2, CR3, NEF, ARW, DNG, RAF and other formats work." },
    { title: "Pick up", text: "Within a minute or two, the watcher on your PC downloads it and renders darktable's defaults as the starting point." },
    { title: "Edit", text: "Claude looks at each render, adjusts the edit and renders again, then judges the result before it exports." },
    { title: "Deliver", text: "The full-size JPEG goes to Processed. The raw moves to Archive with its darktable sidecar and Claude's notes." },
];

export const outputsNote = "In `Photos/Processing`, next to the inbox. A raw that fails twice goes to Failed; move it back to retry.";

export const outputs: { name: string; folder: string; contents: string }[] = [
    { name: "IMG_4899.jpg", folder: "Processed", contents: "The finished JPEG at full resolution" },
    { name: "IMG_4899.CR2", folder: "Archive", contents: "The raw, moved out of the inbox" },
    { name: "IMG_4899.CR2.xmp", folder: "Archive", contents: "The edit as darktable history" },
    { name: "IMG_4899.notes.md", folder: "Archive", contents: "What Claude saw, what it changed and why" },
    { name: "IMG_4899.CR2.error.txt", folder: "Failed", contents: "Why the edit failed, next to the raw" },
];

// The recipe's sections and the darktable module each one compiles to (src/PhotoProcessing.Core).
export const adjustments: [string, string][] = [
    ["Exposure and black level", "exposure"],
    ["White balance", "color calibration"],
    ["Tone mapping", "sigmoid"],
    ["Shadows and highlights", "tone equalizer"],
    ["Colour and grading", "color balance rgb"],
    ["Local contrast", "local contrast"],
    ["Noise", "denoise (profiled)"],
    ["Sharpening", "sharpen"],
    ["Straightening", "rotate and perspective"],
    ["Crop", "crop"],
];

export const steering: { title: string; text: string }[] = [
    { title: "Give instructions", text: "Upload a text file with the same name next to the raw." },
    { title: "Re-edit with Claude", text: "Open the editor workspace in Claude Code and ask for changes, such as “make it a bit warmer and less cropped, then finalize”." },
    { title: "Refine it yourself", text: "Open the archived raw in darktable. Claude's edit loads as history you can change step by step." },
];

export const requirements: { label: string; text: string }[] = [
    { label: "PC", text: "Windows, with the .NET 10 SDK" },
    { label: "darktable", text: "5.6" },
    { label: "Claude Code", text: "Signed in with your Claude plan. Each photo counts toward its usage limits." },
    { label: "Nextcloud", text: "An account that signs in with [Heimdall](https://heimdall.strabix.com), with a `Photos/Processing/Inbox` folder" },
];

export const background = "A scheduled task starts the watcher at boot, before anyone signs in. Photos are edited one at a time, and the tray icon shows what it's doing.";

export const watcherStates: { label: string; tone: "success" | "info" | "warning" | "danger"; text: string }[] = [
    { label: "Watching", tone: "success", text: "The inbox is checked for new photos." },
    { label: "Editing", tone: "info", text: "Claude is working on a photo. The tooltip shows the step." },
    { label: "No recent check", tone: "warning", text: "The inbox hasn't been checked for a while." },
    { label: "Error", tone: "danger", text: "Something needs you, such as signing in to Heimdall again." },
];

// The page's sections, by element id.
export const sections = {
    top: "The headline and the forest example",
    examples: "More example edits",
    "how-it-works": "How a photo goes from upload to JPEG",
    output: "The files that come back",
    adjustments: "What Claude adjusts",
    steer: "Instructions and re-editing",
    background: "The background watcher and its tray icon",
    requirements: "What you need to run it",
} as const;

export type Part = { code: string } | { link: string; href: string } | string;

// Splits `code` and [text](url) out of a text.
export function parts(text: string): Part[] {
    return text.split(/(`[^`]+`|\[[^\]]+\]\([^)]+\))/).filter(Boolean).map((part) => {
        if (part.startsWith("`")) return { code: part.slice(1, -1) };
        const link = /^\[([^\]]+)\]\(([^)]+)\)$/.exec(part);
        return link ? { link: link[1], href: link[2] } : part;
    });
}

export function plainText(text: string) {
    return parts(text).map((part) =>
        typeof part === "string" ? part : "code" in part ? part.code : `${part.link} (${part.href})`
    ).join("");
}
