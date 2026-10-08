"use client";

// A before/after slider: the edit underneath, the "before" image on top, clipped at the handle.
// Drag anywhere on the photo, or focus it and use the arrow keys (a visually hidden range input).

import { useRef, useState, type PointerEvent } from "react";
import { cx } from "@/components/ui";

export function Compare({ before, after, alt, beforeLabel = "darktable defaults", className, labels = true }: {
    before: string;
    beforeLabel?: string;
    after: string;
    alt: string;
    className?: string;
    labels?: boolean;
}) {
    const [position, setPosition] = useState(50);
    const frame = useRef<HTMLDivElement>(null);

    function track(event: PointerEvent<HTMLDivElement>) {
        const box = frame.current?.getBoundingClientRect();
        if (!box) return;
        setPosition(Math.min(100, Math.max(0, ((event.clientX - box.left) / box.width) * 100)));
    }

    return (
        <div ref={frame}
             className={cx("group relative aspect-[3/2] cursor-ew-resize touch-pan-y overflow-hidden rounded-lg border border-line bg-subtle shadow-card select-none", className)}
             onPointerDown={(event) => {
                 event.currentTarget.setPointerCapture(event.pointerId);
                 track(event);
             }}
             onPointerMove={(event) => {
                 if (event.currentTarget.hasPointerCapture(event.pointerId)) track(event);
             }}>
            {/* eslint-disable-next-line @next/next/no-img-element -- a static export serves images as is */}
            <img src={after} alt={`${alt}, edited by Claude`} draggable={false}
                 className="absolute inset-0 size-full object-cover" />
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src={before} alt={`${alt}, ${beforeLabel}`} draggable={false}
                 className="absolute inset-0 size-full object-cover"
                 style={{ clipPath: `inset(0 ${100 - position}% 0 0)` }} />

            {labels && (
                <>
                    <span className="pointer-events-none absolute top-3 left-3 rounded bg-surface/90 px-1.5 py-0.5 text-xs font-medium text-fg shadow-card">
                        {beforeLabel}
                    </span>
                    <span className="pointer-events-none absolute top-3 right-3 rounded bg-surface/90 px-1.5 py-0.5 text-xs font-medium text-fg shadow-card">
                        Claude&apos;s edit
                    </span>
                </>
            )}

            <input type="range" min={0} max={100} step={1} value={Math.round(position)}
                   onChange={(event) => setPosition(Number(event.target.value))}
                   aria-label={`Compare ${alt}: ${beforeLabel} on the left, Claude's edit on the right`}
                   className="sr-only" />
            <div className="pointer-events-none absolute inset-y-0 w-0.5 -translate-x-1/2 bg-white/90 shadow-[0_0_0_1px_rgb(0_0_0/0.15)]"
                 style={{ left: `${position}%` }}>
                <span className="absolute top-1/2 left-1/2 flex size-8 -translate-x-1/2 -translate-y-1/2 items-center justify-center gap-1 rounded-full border border-line bg-surface shadow-card group-has-[input:focus-visible]:shadow-[0_0_0_3px_var(--ring)]">
                    <span className="h-3 w-0.5 rounded-full bg-faint" />
                    <span className="h-3 w-0.5 rounded-full bg-faint" />
                </span>
            </div>
        </div>
    );
}
