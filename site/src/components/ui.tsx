// The Heimdall family's server-safe pieces (the heimdall-style skill's components.md), as far as
// this site uses them.

import type { ReactNode } from "react";
import { parts } from "@/content";

export function cx(...classes: (string | false | null | undefined)[]) {
    return classes.filter(Boolean).join(" ");
}

type ButtonVariant = "primary" | "secondary" | "ghost";

export function buttonClass(variant: ButtonVariant = "secondary", size: "sm" | "md" = "md") {
    return cx(
        "inline-flex items-center justify-center gap-1.5 whitespace-nowrap rounded-md font-medium transition-colors",
        size === "sm" ? "h-7 px-2.5 text-[13px] [&_svg]:size-3.5" : "h-8 px-3 text-sm [&_svg]:size-4",
        variant === "primary" && "bg-primary text-on-primary shadow-card hover:bg-primary-hover",
        variant === "secondary" && "border border-line bg-surface text-fg hover:bg-subtle",
        variant === "ghost" && "text-muted hover:bg-subtle hover:text-fg"
    );
}

export function Card({ id, children, className }: { id?: string; children: ReactNode; className?: string }) {
    return (
        <section id={id} aria-labelledby={id && `${id}-title`}
                 className={cx("rounded-lg border border-line bg-surface shadow-card", className)}>
            {children}
        </section>
    );
}

export function CardHeader({ id, title, description, actions }: {
    id?: string;
    title: string;
    description?: ReactNode;
    actions?: ReactNode;
}) {
    return (
        <div className="flex flex-wrap items-start justify-between gap-3 border-b border-line-subtle px-5 py-4">
            <div className="min-w-0">
                <h2 id={id && `${id}-title`} className="text-[15px] font-semibold">{title}</h2>
                {description && <p className="mt-0.5 text-[13px] text-muted">{description}</p>}
            </div>
            {actions && <div className="flex items-center gap-2">{actions}</div>}
        </div>
    );
}

export function CardBody({ children, className }: { children: ReactNode; className?: string }) {
    return <div className={cx("px-5 py-4", className)}>{children}</div>;
}

type Tone = "neutral" | "success" | "warning" | "danger" | "info";

const toneClass: Record<Tone, string> = {
    neutral: "border border-line bg-subtle text-muted",
    success: "bg-success-soft text-success",
    warning: "bg-warning-soft text-warning",
    danger: "bg-danger-soft text-danger",
    info: "bg-info-soft text-info",
};

export function Badge({ tone = "neutral", dot, children }: { tone?: Tone; dot?: boolean; children: ReactNode }) {
    return (
        <span className={cx("inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-xs font-medium whitespace-nowrap",
            toneClass[tone])}>
            {dot && <span className="size-1.5 rounded-full bg-current" />}
            {children}
        </span>
    );
}

export function Stat({ label, icon, value, note }: { label: string; icon: ReactNode; value: string; note: string }) {
    return (
        <div className="rounded-lg border border-line bg-surface p-4 shadow-card">
            <div className="flex items-center justify-between gap-2 text-[13px] text-muted">
                {label} <span className="text-faint [&_svg]:size-4">{icon}</span>
            </div>
            <div className="mt-2 text-[26px] leading-none font-semibold tracking-tight">{value}</div>
            <div className="mt-2 text-xs text-muted">{note}</div>
        </div>
    );
}

// Key/value: the label column is 180px from sm up, stacked below.
export function KeyValue({ items }: { items: { label: ReactNode; value: ReactNode }[] }) {
    return (
        <dl className="grid grid-cols-1 gap-x-6 gap-y-1 sm:grid-cols-[180px_1fr] sm:gap-y-3">
            {items.map(({ label, value }, i) => (
                <div key={i} className="contents">
                    <dt className={cx("text-[13px] text-muted", i > 0 && "mt-2 sm:mt-0")}>{label}</dt>
                    <dd className="min-w-0 text-sm break-words">{value}</dd>
                </div>
            ))}
        </dl>
    );
}

export function Code({ children }: { children: ReactNode }) {
    return <code className="rounded bg-subtle px-1 py-0.5 font-mono text-[12.5px]">{children}</code>;
}

export function Th({ children }: { children: ReactNode }) {
    return (
        <th scope="col"
            className="border-b border-line bg-subtle px-4 py-2 text-xs font-semibold tracking-wide whitespace-nowrap text-muted uppercase">
            {children}
        </th>
    );
}

export function Td({ children, muted }: { children: ReactNode; muted?: boolean }) {
    return (
        <td className={cx("border-b border-line-subtle px-4 py-3 align-middle [tr:last-child>&]:border-b-0",
            muted && "text-muted")}>
            {children}
        </td>
    );
}

// Renders a text from content.ts, with its `code` and [links](url).
export function Rich({ text }: { text: string }) {
    return parts(text).map((part, i) =>
        typeof part === "string" ? part
            : "code" in part ? <Code key={i}>{part.code}</Code>
            : <a key={i} href={part.href} className="text-link hover:underline">{part.link}</a>
    );
}
