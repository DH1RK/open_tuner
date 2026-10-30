import type { OpenTunerState } from "./client.js";

/** The tuner colours of OpenTuner (InstrumentLook.TunerColors): RX 1 blue, RX 2 green, RX 3 orange, RX 4 violet. */
export const TunerColors = ["#0078d7", "#009900", "#ff8c00", "#9400d3"];

const Off = "#303030";
const Muted = "#c0392b";
const Text = "#ffffff";

export function tunerColor(tuner: number): string {
	return tuner >= 1 ? TunerColors[(tuner - 1) % TunerColors.length] : Off;
}

/** A key image (144x144 SVG): a big label and a small line below. */
export function keyImage(label: string, sub: string, background: string, options: { dim?: boolean; frame?: string } = {}): string {
	const opacity = options.dim ? 0.45 : 1;
	const frame = options.frame ? `<rect x="5" y="5" width="134" height="134" rx="14" fill="none" stroke="${options.frame}" stroke-width="8"/>` : "";
	const svg =
		`<svg xmlns="http://www.w3.org/2000/svg" width="144" height="144" viewBox="0 0 144 144">` +
		`<rect width="144" height="144" fill="#101010"/>` +
		`<rect x="4" y="4" width="136" height="136" rx="16" fill="${background}" opacity="${opacity}"/>${frame}` +
		`<text x="72" y="82" font-family="Segoe UI, Arial, sans-serif" font-size="${labelSize(label)}" font-weight="700" fill="${Text}" text-anchor="middle">${escapeXml(label)}</text>` +
		`<text x="72" y="116" font-family="Segoe UI, Arial, sans-serif" font-size="22" fill="${Text}" text-anchor="middle">${escapeXml(sub)}</text>` +
		`</svg>`;

	return "data:image/svg+xml;charset=utf8," + encodeURIComponent(svg);
}

/** Font size of the big label: the tuner keys (RX 1 to 4) are big, all the others (mute, tabs, views) share one size. */
function labelSize(label: string): number {
	return label.startsWith("RX") ? 44 : 30;
}

function escapeXml(text: string): string {
	return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

/** "selected" or "1".."4" of the settings -> tuner number (1 based), 0 = none / not available */
export function resolveTuner(setting: string | undefined, state: OpenTunerState | null): number {
	if (!state) {
		return 0;
	}

	const tuner = setting && setting !== "selected" ? Number(setting) : state.selected;
	return tuner >= 1 && tuner <= state.tuners ? tuner : 0;
}

export const Colors = { Off, Muted };
