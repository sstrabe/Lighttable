import type { Metadata, Viewport } from "next";
import "./globals.css";

export const metadata: Metadata = {
    metadataBase: new URL("https://lighttable.sstrabe.dev"),
    title: { default: "Lighttable", template: "%s · Lighttable" },
    description: "Upload a raw photo to Nextcloud and get back a finished JPEG, edited by Claude in darktable.",
    icons: { icon: "/logo/favicon.ico", apple: "/logo/mark-180.png" },
    openGraph: {
        type: "website",
        siteName: "Lighttable",
        title: "Lighttable",
        description: "Raw photos developed in darktable, with Claude doing the edits.",
        images: "/logo/mark-512.png",
    },
};

export const viewport: Viewport = {
    colorScheme: "light dark",
    // The browser chrome in the page colour (--page in globals.css).
    themeColor: [
        { media: "(prefers-color-scheme: light)", color: "#f2f3f5" },
        { media: "(prefers-color-scheme: dark)", color: "#141518" },
    ],
};

export default function RootLayout({ children }: LayoutProps<"/">) {
    return (
        <html lang="en" className="antialiased">
            <body className="min-h-dvh bg-page font-sans text-fg">{children}</body>
        </html>
    );
}
