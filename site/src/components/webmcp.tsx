/// <reference types="webmcp-types" />
"use client";

// WebMCP (https://webmachinelearning.github.io/webmcp): tools that let an AI agent in the visitor's
// browser read the page as data and point the visitor at parts of it. They are registered on
// document.modelContext, or on navigator.modelContext, its name in Chrome's early previews and in
// polyfills. Browsers without either get nothing, and the page is unchanged.

import { useEffect } from "react";
import {
    adjustments,
    background,
    gallery,
    hero,
    intro,
    outputs,
    outputsNote,
    plainText,
    repo,
    requirements,
    sections,
    setupGuide,
    steering,
    steps,
    summary,
    watcherStates,
    type ExampleEdit,
} from "@/content";

type LegacyModelContext = WebMCP.ModelContext & { unregisterTool?: (name: string) => void };

const examples = [hero, ...gallery];

// Anchors an agent can scroll to: the page's sections and each example.
const places: Record<string, string> = {
    ...sections,
    ...Object.fromEntries(examples.map((e) => [`example-${e.id}`, `The ${e.alt.toLowerCase()} example`])),
};
// The hero is the top of the page, not a figure of its own.
delete places[`example-${hero.id}`];

function json(value: unknown) {
    return JSON.stringify(value, null, 2);
}

function absolute(path: string) {
    return new URL(path, location.origin).href;
}

function describeExample(e: ExampleEdit) {
    return {
        id: e.id,
        subject: e.alt,
        instructions: e.instructions ?? null,
        notes: e.caption,
        darktableModules: e.modules,
        before: { label: e.beforeLabel, url: absolute(e.before) },
        after: { label: "Claude's edit", url: absolute(e.after) },
        anchor: e.id === hero.id ? "top" : `example-${e.id}`,
    };
}

const tools: WebMCP.ModelContextTool[] = [
    {
        name: "get_lighttable_overview",
        title: "Lighttable overview",
        description:
            "Describes Lighttable: what it does, how a raw photo goes from a Nextcloud upload to a finished JPEG, " +
            "the files that come back, the darktable modules Claude adjusts, how to steer an edit, the background " +
            "watcher, the requirements, and links to the source and setup guide.",
        inputSchema: { type: "object", properties: {} },
        annotations: { readOnlyHint: true },
        execute: () => json({
            name: "Lighttable",
            summary,
            intro,
            howItWorks: steps.map((s, i) => ({ step: i + 1, title: s.title, text: plainText(s.text) })),
            whatComesBack: { where: plainText(outputsNote), files: outputs },
            adjustments: adjustments.map(([adjustment, darktableModule]) => ({ adjustment, darktableModule })),
            steering,
            background: { text: background, trayStates: watcherStates.map(({ label, text }) => ({ label, text })) },
            requirements: requirements.map((r) => ({ label: r.label, text: plainText(r.text) })),
            links: { site: location.origin, source: repo, setupGuide },
        }),
    },
    {
        name: "list_example_edits",
        title: "Example edits",
        description:
            "Lists the real edits shown on the page: the subject, any instructions the photographer gave, " +
            "lines from Claude's notes on what it saw and changed, the darktable modules it used, and URLs of the " +
            "camera's JPEG and Claude's edit.",
        inputSchema: { type: "object", properties: {} },
        annotations: { readOnlyHint: true },
        execute: () => json({ examples: examples.map(describeExample) }),
    },
    {
        name: "show_on_page",
        title: "Show on page",
        description: "Scrolls the page to a section or an example edit, to show it to the visitor.",
        inputSchema: {
            type: "object",
            properties: {
                place: {
                    type: "string",
                    enum: Object.keys(places),
                    description: Object.entries(places).map(([id, what]) => `${id}: ${what}`).join("; "),
                },
            },
            required: ["place"],
        },
        annotations: { readOnlyHint: true },
        execute: (input: { place?: unknown }) => {
            const place = String(input.place);
            const target = place in places ? document.getElementById(place) : null;
            if (!target) return `Unknown place "${place}". Use one of: ${Object.keys(places).join(", ")}.`;
            const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
            target.scrollIntoView({ behavior: reduce ? "auto" : "smooth", block: "start" });
            history.replaceState(null, "", `#${place}`);
            return `Scrolled to ${places[place].toLowerCase()}.`;
        },
    },
];

export function WebMcpTools() {
    useEffect(() => {
        const context = (document.modelContext ??
            (navigator as Navigator & { modelContext?: LegacyModelContext }).modelContext) as LegacyModelContext | undefined;
        if (typeof context?.registerTool !== "function") return;

        const registration = new AbortController();
        for (const tool of tools) {
            // A tool left over from a previous registration (a dev reload) makes registerTool throw.
            Promise.resolve()
                .then(() => context.registerTool(tool, { signal: registration.signal }))
                .catch((error: unknown) => console.warn(`WebMCP: couldn't register ${tool.name}`, error));
        }
        return () => {
            registration.abort();
            // Early previews unregister by name and ignore the signal.
            for (const tool of tools) {
                try { context.unregisterTool?.(tool.name); } catch { /* already gone */ }
            }
        };
    }, []);

    return null;
}
