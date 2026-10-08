import type { Metadata } from "next";
import {
    BookOpen,
    CodeXml,
    FileCode,
    FileImage,
    FileText,
    FileX,
    FolderCheck,
    HardDriveDownload,
    SlidersHorizontal,
    Upload,
} from "lucide-react";
import type { ReactNode } from "react";
import { Compare } from "@/components/compare";
import { Lockup } from "@/components/logo";
import { Badge, buttonClass, cx, KeyValue, Rich } from "@/components/ui";
import { WebMcpTools } from "@/components/webmcp";
import { adjustments, gallery, hero, intro, outputs, outputsNote, repo, requirements, setupGuide, steering, steps, background, watcherStates } from "@/content";

export const metadata: Metadata = {
    alternates: { canonical: "/" },
};

// The text and photos are in content.ts, shared with the page's WebMCP tools.
const stepIcons: ReactNode[] = [<Upload key="upload" />, <HardDriveDownload key="pick-up" />, <SlidersHorizontal key="edit" />, <FolderCheck key="deliver" />];
const outputIcons: Record<string, ReactNode> = {
    ".jpg": <FileImage />,
    ".CR2": <FileImage />,
    ".xmp": <FileCode />,
    ".md": <FileText />,
    ".txt": <FileX />,
};

function Section({ id, title, description, children, className }: {
    id: string;
    title: string;
    description?: ReactNode;
    children: ReactNode;
    className?: string;
}) {
    return (
        <section id={id} aria-labelledby={`${id}-title`} className={cx("scroll-mt-20", className)}>
            <h2 id={`${id}-title`} className="text-[18px] font-semibold tracking-tight">{title}</h2>
            {description && <p className="mt-1 max-w-2xl text-muted">{description}</p>}
            <div className="mt-5">{children}</div>
        </section>
    );
}

export default function Home() {
    return (
        <>
            <header className="sticky top-0 z-10 border-b border-line bg-surface">
                <div className="mx-auto flex h-14 w-full max-w-6xl items-center justify-between gap-4 px-4 lg:px-8">
                    <a href="#top" aria-label="Lighttable">
                        {/* The lockup's 6-unit margin (of 66) would indent it from the content below. */}
                        <Lockup height={32} className="-ml-[3px] block" />
                    </a>
                    <nav aria-label="Sections" className="flex items-center gap-1">
                        <a href="#how-it-works" className={cx(buttonClass("ghost", "sm"), "max-sm:hidden")}>How it works</a>
                        <a href="#output" className={cx(buttonClass("ghost", "sm"), "max-sm:hidden")}>What comes back</a>
                        <a href="#requirements" className={cx(buttonClass("ghost", "sm"), "max-md:hidden")}>Requirements</a>
                        <a href={repo} className={buttonClass("secondary", "sm")}><CodeXml /> GitHub</a>
                    </nav>
                </div>
            </header>

            <main id="top" className="mx-auto w-full max-w-6xl space-y-16 px-4 py-8 lg:px-8 lg:py-12">
                <section aria-labelledby="hero-title">
                    <div className="grid items-end gap-x-10 gap-y-4 lg:grid-cols-[1fr_auto]">
                        <div>
                            <h1 id="hero-title" className="max-w-3xl text-[30px] leading-[1.15] font-semibold tracking-tight sm:text-[36px]">
                                Upload a raw. Get back a finished photo, edited by Claude in darktable.
                            </h1>
                            <p className="mt-3 max-w-2xl text-[15px] text-muted">{intro}</p>
                        </div>
                        <div className="flex flex-wrap items-center gap-2">
                            <a href={setupGuide} className={buttonClass("primary")}><BookOpen /> Read the setup guide</a>
                            <a href="#how-it-works" className={buttonClass("secondary")}>See how it works</a>
                        </div>
                    </div>

                    <Compare before={hero.before} after={hero.after} alt={hero.alt} beforeLabel={hero.beforeLabel} className="mt-8" />
                    <div className="mt-3 flex flex-wrap items-start justify-between gap-x-8 gap-y-2">
                        <p className="max-w-3xl text-sm">{hero.caption}</p>
                        <p className="text-[13px] text-muted">Drag across the photo to compare.</p>
                    </div>
                    <p className="mt-2 flex flex-wrap gap-1.5">
                        {hero.modules.map((m) => <Badge key={m}>{m}</Badge>)}
                    </p>
                </section>

                <Section id="examples" title="More examples">
                    <div className="grid gap-6 md:grid-cols-2">
                        {gallery.map((photo) => (
                            <figure key={photo.id} id={`example-${photo.id}`} className="scroll-mt-20">
                                <Compare before={photo.before} after={photo.after} alt={photo.alt} beforeLabel={photo.beforeLabel} />
                                <figcaption className="mt-3">
                                    <p className="text-sm">{photo.caption}</p>
                                    <p className="mt-2 flex flex-wrap gap-1.5">
                                        {photo.modules.map((m) => <Badge key={m}>{m}</Badge>)}
                                    </p>
                                </figcaption>
                            </figure>
                        ))}
                    </div>
                </Section>

                <Section id="how-it-works" title="How it works">
                    <ol className="grid gap-x-8 gap-y-6 sm:grid-cols-2 lg:grid-cols-4">
                        {steps.map((step, i) => (
                            <li key={step.title} className="border-t border-line pt-4">
                                <span className="inline-flex size-8 items-center justify-center rounded-md bg-primary-soft text-link [&_svg]:size-4">
                                    {stepIcons[i]}
                                </span>
                                <h3 className="mt-3 text-sm font-semibold">
                                    <span className="font-medium text-muted">{i + 1}.</span> {step.title}
                                </h3>
                                <p className="mt-1 text-[13px] text-muted"><Rich text={step.text} /></p>
                            </li>
                        ))}
                    </ol>
                </Section>

                <div className="grid items-start gap-x-10 gap-y-16 lg:grid-cols-[3fr_2fr]">
                    <Section id="output" title="What comes back"
                             description={<Rich text={outputsNote} />}>
                        <ul className="divide-y divide-line-subtle rounded-lg border border-line bg-surface shadow-card">
                            {outputs.map((file) => (
                                <li key={file.name} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-3">
                                    <span className="text-faint [&_svg]:size-4">{outputIcons[file.name.slice(file.name.lastIndexOf("."))]}</span>
                                    <span className="font-mono text-[12.5px]">{file.name}</span>
                                    <Badge>{file.folder}</Badge>
                                    <span className="w-full text-[13px] text-muted sm:ml-auto sm:w-auto">{file.contents}</span>
                                </li>
                            ))}
                        </ul>
                    </Section>

                    <Section id="adjustments" title="What Claude adjusts"
                             description="Each adjustment is a darktable module in the photo's history.">
                        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
                            {adjustments.map(([adjustment, darktableModule]) => (
                                <div key={adjustment} className="contents">
                                    <dt>{adjustment}</dt>
                                    <dd className="text-muted">{darktableModule}</dd>
                                </div>
                            ))}
                        </dl>
                    </Section>
                </div>

                <Section id="steer" title="Steer it, or take it further"
                         description="Claude follows your instructions over its own taste.">
                    <div className="grid gap-6 lg:grid-cols-3">
                        <div className="rounded-lg border border-line bg-surface p-5 shadow-card">
                            <h3 className="text-sm font-semibold">{steering[0].title}</h3>
                            <p className="mt-1 text-[13px] text-muted">{steering[0].text}</p>
                            <ul className="mt-3 divide-y divide-line-subtle rounded-md border border-line bg-subtle font-mono text-[12.5px]">
                                <li className="flex items-center gap-2 px-3 py-2">
                                    <FileImage className="size-4 text-faint" /> Inbox/IMG_4899.CR2
                                </li>
                                <li className="px-3 py-2">
                                    <span className="flex items-center gap-2">
                                        <FileText className="size-4 text-faint" /> Inbox/IMG_4899.txt
                                    </span>
                                    <span className="mt-1 block pl-6 font-sans text-[13px]">moody, crop to 4:5</span>
                                </li>
                            </ul>
                        </div>
                        {steering.slice(1).map((way) => (
                            <div key={way.title} className="rounded-lg border border-line bg-surface p-5 shadow-card">
                                <h3 className="text-sm font-semibold">{way.title}</h3>
                                <p className="mt-1 text-[13px] text-muted">{way.text}</p>
                            </div>
                        ))}
                    </div>
                </Section>

                <div className="grid items-start gap-x-10 gap-y-16 lg:grid-cols-2">
                    <Section id="background" title="Runs in the background"
                             description={background}>
                        <KeyValue items={watcherStates.map((state) => (
                            { label: <Badge tone={state.tone} dot>{state.label}</Badge>, value: state.text }
                        ))} />
                    </Section>

                    <Section id="requirements" title="Requirements">
                        <KeyValue items={requirements.map((r) => ({ label: r.label, value: <Rich text={r.text} /> }))} />
                        <a href={setupGuide} className={cx(buttonClass("secondary"), "mt-5")}><BookOpen /> Read the setup guide</a>
                    </Section>
                </div>
            </main>

            <WebMcpTools />

            <footer className="mx-auto w-full max-w-6xl px-4 pb-8 lg:px-8">
                <div className="flex flex-wrap justify-between gap-x-6 gap-y-2 border-t border-line pt-4 text-[13px] text-muted">
                    <span>
                        Lighttable · Made by <a href="https://github.com/sstrabe" className="text-link hover:underline">sstrabe</a>
                    </span>
                    <a href={repo} className="text-link hover:underline">Source on GitHub</a>
                </div>
            </footer>
        </>
    );
}
