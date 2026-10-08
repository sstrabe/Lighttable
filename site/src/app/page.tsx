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
import { Badge, buttonClass, Code, cx, KeyValue } from "@/components/ui";

export const metadata: Metadata = {
    alternates: { canonical: "/" },
};

const repo = "https://github.com/sstrabe/Lighttable";
const setupGuide = `${repo}#setup`;

// A real edit: the camera's own JPEG and Claude's edit of the raw, made without instructions.
const hero = { before: "/photos/forest-before.jpg", after: "/photos/forest-after.jpg", alt: "A forest valley in the mountains", beforeLabel: "Camera JPEG" };

const gallery: { before: string; after: string; alt: string; beforeLabel: string; caption: string; modules?: string[] }[] = [
    {
        // A real edit, the camera's JPEG against Claude's, with lines from its notes.md.
        before: "/photos/contrail-before.jpg",
        after: "/photos/contrail-after.jpg",
        alt: "A contrail over trees at dusk",
        beforeLabel: "Camera JPEG",
        caption: "No instructions. From Claude's notes: “The sky looked slightly grey and flat.” Claude added vibrance and contrast for a cleaner twilight blue and kept the as-shot white balance. It tried deeper blacks first, saw they erased the meadow, and backed off.",
        modules: ["exposure", "sigmoid", "color balance rgb"],
    },
    {
        // A real edit, the camera's JPEG against Claude's, with lines from its notes.md.
        before: "/photos/dusk-before.jpg",
        after: "/photos/dusk-after.jpg",
        alt: "Dusk over the mountains",
        beforeLabel: "Camera JPEG",
        caption: "Instructions: “make the sunset pop”. From Claude's notes: “The sky was slightly hazy and flat and the sunset glow looked washed out.” Claude warmed the white balance with a touch of magenta to keep the salmon-pink glow, and lifted the shadows gently to separate the ridges.",
        modules: ["exposure", "color calibration", "sigmoid", "color balance rgb", "local contrast", "tone equalizer"],
    },
];

const steps: { icon: ReactNode; title: string; text: ReactNode }[] = [
    {
        icon: <Upload />,
        title: "Upload",
        text: <>Put a raw file in <Code>Photos/Processing/Inbox</Code> on Nextcloud. CR2, CR3, NEF, ARW, DNG, RAF and other formats work.</>,
    },
    {
        icon: <HardDriveDownload />,
        title: "Pick up",
        text: "Within a minute or two, the watcher on your PC downloads it and renders darktable's defaults as the starting point.",
    },
    {
        icon: <SlidersHorizontal />,
        title: "Edit",
        text: "Claude looks at each render, adjusts the edit and renders again, then judges the result before it exports.",
    },
    {
        icon: <FolderCheck />,
        title: "Deliver",
        text: "The full-size JPEG goes to Processed. The raw moves to Archive with its darktable sidecar and Claude's notes.",
    },
];

const outputs: { icon: ReactNode; name: string; folder: string; contents: string }[] = [
    { icon: <FileImage />, name: "IMG_4899.jpg", folder: "Processed", contents: "The finished JPEG at full resolution" },
    { icon: <FileImage />, name: "IMG_4899.CR2", folder: "Archive", contents: "The raw, moved out of the inbox" },
    { icon: <FileCode />, name: "IMG_4899.CR2.xmp", folder: "Archive", contents: "The edit as darktable history" },
    { icon: <FileText />, name: "IMG_4899.notes.md", folder: "Archive", contents: "What Claude saw, what it changed and why" },
    { icon: <FileX />, name: "IMG_4899.CR2.error.txt", folder: "Failed", contents: "Why the edit failed, next to the raw" },
];

// The recipe's sections and the darktable module each one compiles to (src/PhotoProcessing.Core).
const adjustments: [string, string][] = [
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
                            <p className="mt-3 max-w-2xl text-[15px] text-muted">
                                Drop a raw file in Nextcloud and a JPEG comes back a few minutes later. The edit comes
                                with it as darktable history, so you can open the raw and take it further yourself.
                            </p>
                        </div>
                        <div className="flex flex-wrap items-center gap-2">
                            <a href={setupGuide} className={buttonClass("primary")}><BookOpen /> Read the setup guide</a>
                            <a href="#how-it-works" className={buttonClass("secondary")}>See how it works</a>
                        </div>
                    </div>

                    <Compare {...hero} className="mt-8" />
                    <p className="mt-3 text-[13px] text-muted">
                        Edited without instructions. Drag across the photo to compare the camera&apos;s own JPEG with Claude&apos;s edit.
                    </p>
                </section>

                <Section id="examples" title="More examples">
                    <div className="grid gap-6 md:grid-cols-2">
                        {gallery.map((photo) => (
                            <figure key={photo.alt}>
                                <Compare before={photo.before} after={photo.after} alt={photo.alt} beforeLabel={photo.beforeLabel} />
                                <figcaption className="mt-3">
                                    <p className="text-sm">{photo.caption}</p>
                                    {photo.modules && (
                                        <p className="mt-2 flex flex-wrap gap-1.5">
                                            {photo.modules.map((m) => <Badge key={m}>{m}</Badge>)}
                                        </p>
                                    )}
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
                                    {step.icon}
                                </span>
                                <h3 className="mt-3 text-sm font-semibold">
                                    <span className="font-medium text-muted">{i + 1}.</span> {step.title}
                                </h3>
                                <p className="mt-1 text-[13px] text-muted">{step.text}</p>
                            </li>
                        ))}
                    </ol>
                </Section>

                <div className="grid items-start gap-x-10 gap-y-16 lg:grid-cols-[3fr_2fr]">
                    <Section id="output" title="What comes back"
                             description={<>In <Code>Photos/Processing</Code>, next to the inbox. A raw that fails twice goes to Failed; move it back to retry.</>}>
                        <ul className="divide-y divide-line-subtle rounded-lg border border-line bg-surface shadow-card">
                            {outputs.map((file) => (
                                <li key={file.name} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-3">
                                    <span className="text-faint [&_svg]:size-4">{file.icon}</span>
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
                            <h3 className="text-sm font-semibold">Give instructions</h3>
                            <p className="mt-1 text-[13px] text-muted">Upload a text file with the same name next to the raw.</p>
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
                        <div className="rounded-lg border border-line bg-surface p-5 shadow-card">
                            <h3 className="text-sm font-semibold">Re-edit with Claude</h3>
                            <p className="mt-1 text-[13px] text-muted">
                                Open the editor workspace in Claude Code and ask for changes, such as “make it a bit
                                warmer and less cropped, then finalize”.
                            </p>
                        </div>
                        <div className="rounded-lg border border-line bg-surface p-5 shadow-card">
                            <h3 className="text-sm font-semibold">Refine it yourself</h3>
                            <p className="mt-1 text-[13px] text-muted">
                                Open the archived raw in darktable. Claude&apos;s edit loads as history you can change
                                step by step.
                            </p>
                        </div>
                    </div>
                </Section>

                <div className="grid items-start gap-x-10 gap-y-16 lg:grid-cols-2">
                    <Section id="background" title="Runs in the background"
                             description="A scheduled task starts the watcher at boot, before anyone signs in. Photos are edited one at a time, and the tray icon shows what it's doing.">
                        <KeyValue items={[
                            { label: <Badge tone="success" dot>Watching</Badge>, value: "The inbox is checked for new photos." },
                            { label: <Badge tone="info" dot>Editing</Badge>, value: "Claude is working on a photo. The tooltip shows the step." },
                            { label: <Badge tone="warning" dot>No recent check</Badge>, value: "The inbox hasn't been checked for a while." },
                            { label: <Badge tone="danger" dot>Error</Badge>, value: "Something needs you, such as signing in to Heimdall again." },
                        ]} />
                    </Section>

                    <Section id="requirements" title="Requirements">
                        <KeyValue items={[
                            { label: "PC", value: "Windows, with the .NET 10 SDK" },
                            { label: "darktable", value: "5.6" },
                            { label: "Claude Code", value: "Signed in with your Claude plan. Each photo counts toward its usage limits." },
                            {
                                label: "Nextcloud",
                                value: <>An account that signs in with{" "}
                                    <a href="https://heimdall.strabix.com" className="text-link hover:underline">Heimdall</a>,
                                    with a <Code>Photos/Processing/Inbox</Code> folder</>,
                            },
                        ]} />
                        <a href={setupGuide} className={cx(buttonClass("secondary"), "mt-5")}><BookOpen /> Read the setup guide</a>
                    </Section>
                </div>
            </main>

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
