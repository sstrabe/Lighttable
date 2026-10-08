import type { Metadata } from "next";
import {
    BookOpen,
    Clock,
    CodeXml,
    FileCode,
    FileImage,
    FileText,
    FileX,
    Files,
    FolderCheck,
    HardDriveDownload,
    Layers,
    Monitor,
    SlidersHorizontal,
    Upload,
} from "lucide-react";
import type { ReactNode } from "react";
import { Lockup } from "@/components/logo";
import { Badge, buttonClass, Card, CardBody, CardHeader, Code, KeyValue, Stat, Td, Th } from "@/components/ui";

export const metadata: Metadata = {
    alternates: { canonical: "/" },
};

const repo = "https://github.com/sstrabe/Lighttable";
const setupGuide = `${repo}#setup`;

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
    {
        icon: <FileCode />,
        name: "IMG_4899.CR2.xmp",
        folder: "Archive",
        contents: "The edit as darktable history. Open the raw in darktable to refine it.",
    },
    { icon: <FileText />, name: "IMG_4899.notes.md", folder: "Archive", contents: "What Claude saw, what it changed and why" },
    {
        icon: <FileX />,
        name: "IMG_4899.CR2.error.txt",
        folder: "Failed",
        contents: "Written with the raw when an edit fails twice. Move the raw back to the inbox to retry.",
    },
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

export default function Home() {
    return (
        <main className="mx-auto w-full max-w-6xl space-y-6 px-4 py-6 lg:px-8 lg:py-8">
            <header className="pt-2">
                {/* The lockup's 6-unit margin (of 66) would indent it from the text below. */}
                <Lockup height={49} className="-ml-[4.5px] block" />
                <h1 className="mt-5 text-[22px] leading-tight font-semibold tracking-tight">
                    Raw photos developed in darktable, with Claude doing the edits
                </h1>
                <p className="mt-1 max-w-3xl text-muted">
                    Upload a raw file to Nextcloud and get back a finished JPEG. The edit comes with it as darktable
                    history, so you can open the raw and take it further yourself.
                </p>
                <div className="mt-5 flex flex-wrap items-center gap-2">
                    <a href={setupGuide} className={buttonClass("primary")}><BookOpen /> Read the setup guide</a>
                    <a href={repo} className={buttonClass("secondary")}><CodeXml /> View on GitHub</a>
                </div>
            </header>

            <section aria-label="At a glance" className="grid grid-cols-2 gap-4 lg:grid-cols-4">
                <Stat label="Per photo" icon={<Clock />} value="1–3 min" note="Once the upload has settled" />
                <Stat label="darktable modules" icon={<Layers />} value="10" note="From exposure to crop" />
                <Stat label="Output" icon={<Files />} value="3 files" note="JPEG, darktable sidecar, Claude's notes" />
                <Stat label="Runs on" icon={<Monitor />} value="Your PC" note="In the background, from boot" />
            </section>

            <Card id="how-it-works">
                <CardHeader id="how-it-works" title="How it works" />
                <CardBody>
                    <ol className="grid gap-x-6 gap-y-5 sm:grid-cols-2 lg:grid-cols-4">
                        {steps.map((step, i) => (
                            <li key={step.title}>
                                <span className="inline-flex size-8 items-center justify-center rounded-md border border-line bg-subtle text-muted [&_svg]:size-4">
                                    {step.icon}
                                </span>
                                <h3 className="mt-3 text-sm font-semibold">
                                    <span className="font-medium text-muted">{i + 1}.</span> {step.title}
                                </h3>
                                <p className="mt-1 text-[13px] text-muted">{step.text}</p>
                            </li>
                        ))}
                    </ol>
                </CardBody>
            </Card>

            <Card id="output">
                <CardHeader id="output" title="What comes back"
                            description={<>In <Code>Photos/Processing</Code>, next to the inbox.</>} />
                <div className="overflow-x-auto">
                    <table className="w-full min-w-[42rem] border-collapse text-left text-sm">
                        <thead>
                            <tr><Th>File</Th><Th>Folder</Th><Th>Contents</Th></tr>
                        </thead>
                        <tbody>
                            {outputs.map((file) => (
                                <tr key={file.name}>
                                    <Td>
                                        <span className="flex items-center gap-2 font-mono text-[12.5px] whitespace-nowrap">
                                            <span className="text-faint [&_svg]:size-4">{file.icon}</span>
                                            {file.name}
                                        </span>
                                    </Td>
                                    <Td><Badge>{file.folder}</Badge></Td>
                                    <Td muted>{file.contents}</Td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </Card>

            <div className="grid items-start gap-6 lg:grid-cols-2">
                <Card id="adjustments">
                    <CardHeader id="adjustments" title="What Claude adjusts"
                                description="Each adjustment is a darktable module in the photo's history." />
                    <div className="overflow-x-auto">
                        <table className="w-full border-collapse text-left text-sm">
                            <thead>
                                <tr><Th>Adjustment</Th><Th>darktable module</Th></tr>
                            </thead>
                            <tbody>
                                {adjustments.map(([adjustment, darktableModule]) => (
                                    <tr key={adjustment}><Td>{adjustment}</Td><Td muted>{darktableModule}</Td></tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </Card>

                <Card id="steer">
                    <CardHeader id="steer" title="Steer an edit" />
                    <CardBody className="space-y-3">
                        <p className="text-sm">
                            Upload a text file with the same name next to the raw. Claude follows it over its own taste.
                        </p>
                        <ul className="divide-y divide-line-subtle rounded-md border border-line bg-subtle font-mono text-[12.5px]">
                            <li className="flex items-center gap-2 px-3 py-2">
                                <FileImage className="size-4 text-faint" /> Inbox/IMG_4899.CR2
                            </li>
                            <li className="flex flex-wrap items-center gap-x-4 gap-y-1 px-3 py-2">
                                <span className="flex items-center gap-2">
                                    <FileText className="size-4 text-faint" /> Inbox/IMG_4899.txt
                                </span>
                                <span className="ml-auto font-sans text-[13px]">moody, crop to 4:5</span>
                            </li>
                        </ul>
                    </CardBody>
                    <CardBody className="border-t border-line-subtle">
                        <h3 className="text-[13px] font-semibold">Re-edit with Claude</h3>
                        <p className="mt-1 text-[13px] text-muted">
                            Open the editor workspace in Claude Code and ask for changes, such as “make it a bit warmer
                            and less cropped, then finalize”.
                        </p>
                    </CardBody>
                    <CardBody className="border-t border-line-subtle">
                        <h3 className="text-[13px] font-semibold">Refine it yourself</h3>
                        <p className="mt-1 text-[13px] text-muted">
                            Open the archived raw in darktable. Claude&apos;s edit loads as history you can change step
                            by step.
                        </p>
                    </CardBody>
                </Card>
            </div>

            <div className="grid items-start gap-6 lg:grid-cols-2">
                <Card id="background">
                    <CardHeader id="background" title="Runs in the background"
                                description="A scheduled task starts the watcher at boot, before anyone signs in. Photos are edited one at a time." />
                    <CardBody>
                        <h3 className="text-[13px] font-semibold">Tray icon</h3>
                        <p className="mt-1 text-[13px] text-muted">
                            Shows the watcher&apos;s state, and a notification when a photo is done.
                        </p>
                    </CardBody>
                    <CardBody className="border-t border-line-subtle">
                        <KeyValue items={[
                            { label: <Badge tone="success" dot>Watching</Badge>, value: "The inbox is checked for new photos." },
                            { label: <Badge tone="info" dot>Editing</Badge>, value: "Claude is working on a photo. The tooltip shows the step." },
                            { label: <Badge tone="warning" dot>No recent check</Badge>, value: "The inbox hasn't been checked for a while." },
                            { label: <Badge tone="danger" dot>Error</Badge>, value: "Something needs you, such as signing in to Heimdall again." },
                        ]} />
                    </CardBody>
                </Card>

                <Card id="requirements">
                    <CardHeader id="requirements" title="Requirements"
                                actions={<a href={setupGuide} className={buttonClass("secondary", "sm")}><BookOpen /> Setup guide</a>} />
                    <CardBody>
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
                    </CardBody>
                </Card>
            </div>

            <footer className="mt-10 flex flex-wrap justify-between gap-x-6 gap-y-2 border-t border-line pt-4 text-[13px] text-muted">
                <span>
                    Lighttable · Made by <a href="https://github.com/sstrabe" className="text-link hover:underline">sstrabe</a>
                </span>
                <a href={repo} className="text-link hover:underline">Source on GitHub</a>
            </footer>
        </main>
    );
}
