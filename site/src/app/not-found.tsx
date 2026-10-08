import type { Metadata } from "next";
import Link from "next/link";
import { Logo } from "@/components/logo";
import { buttonClass, cx } from "@/components/ui";

export const metadata: Metadata = {
    title: "Page not found",
    robots: { index: false },
};

export default function NotFound() {
    return (
        <main className="flex min-h-dvh flex-col items-center justify-center px-4 py-12">
            <Link href="/" className="mb-6 flex items-center gap-2.5">
                <Logo size={36} className="shrink-0" />
                <span className="text-xl font-semibold tracking-tight">Lighttable</span>
            </Link>
            <div className="w-full max-w-sm rounded-lg border border-line bg-surface p-6 shadow-card">
                <h1 className="text-[18px] font-semibold">Page not found</h1>
                <p className="mt-1 text-[13px] text-muted">There&apos;s nothing at this address.</p>
                <Link href="/" className={cx(buttonClass("primary"), "mt-5 w-full")}>Go to Lighttable</Link>
            </div>
        </main>
    );
}
