// Lighttable's mark and lockup. The SVG masters are in assets/logo; scripts/copy-logo.mjs copies
// them into public/logo.

import Image from "next/image";

export function Logo({ size, className }: { size: number; className?: string }) {
    return <Image src="/logo/mark.svg" alt="" width={size} height={size} className={className} />;
}

// The mark with the drawn wordmark, in its light or dark version to match the page.
export function Lockup({ height, className }: { height: number; className?: string }) {
    const width = Math.round((height * 290) / 66); // the lockup's viewBox
    return (
        <picture className={className}>
            <source srcSet="/logo/lockup-dark.svg" media="(prefers-color-scheme: dark)" />
            <img src="/logo/lockup.svg" alt="Lighttable" width={width} height={height} className="block" />
        </picture>
    );
}
