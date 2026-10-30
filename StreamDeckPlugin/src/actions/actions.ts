import {
	action,
	type DialAction,
	type DialDownEvent,
	type DialRotateEvent,
	type DidReceiveSettingsEvent,
	type KeyAction,
	type KeyDownEvent,
	SingletonAction,
	type WillAppearEvent
} from "@elgato/streamdeck";

import { client } from "../client.js";
import { Colors, keyImage, resolveTuner, tunerColor } from "../render.js";

export type Settings = {
	/** "selected" or "1".."4" */
	tuner?: string;
	/** Properties, Expert, TS Info, Special, Chip */
	tab?: string;
	/** large, only, grid */
	view?: string;
	/** volume dial: step per tick */
	step?: string;
};

type AnyAction = KeyAction<Settings> | DialAction<Settings>;

/** "tuner" field of a command: left out for "selected", the tuner selected in OpenTuner then decides */
function tunerArgument(setting: string | undefined): Record<string, unknown> {
	return setting && setting !== "selected" ? { tuner: Number(setting) } : {};
}

/** Common part: every action draws itself again when OpenTuner reports a change. */
abstract class OpenTunerAction extends SingletonAction<Settings> {
	protected abstract draw(visible: AnyAction, settings: Settings): void;

	public refreshAll(): void {
		for (const visible of this.actions) {
			visible.getSettings().then((settings) => this.draw(visible as AnyAction, settings));
		}
	}

	public override onWillAppear(ev: WillAppearEvent<Settings>): void {
		this.draw(ev.action as AnyAction, ev.payload.settings);
	}

	public override onDidReceiveSettings(ev: DidReceiveSettingsEvent<Settings>): void {
		this.draw(ev.action as AnyAction, ev.payload.settings);
	}
}

/** Selects the tuner (RX 1 to 4); the key shows its colour, dimmed while there is no lock, framed while it is the selected one. */
@action({ UUID: "com.dh1rk.opentuner.select" })
export class SelectTuner extends OpenTunerAction {
	protected draw(visible: AnyAction, settings: Settings): void {
		if (!visible.isKey()) {
			return;
		}

		const state = client.state;
		const tuner = resolveTuner(settings.tuner ?? "1", state);
		if (!state || !tuner) {
			visible.setImage(keyImage(`RX ${settings.tuner ?? "1"}`, client.connected ? "-" : "off", Colors.Off, { dim: true }));
			return;
		}

		const locked = state.locked[tuner - 1];
		visible.setImage(keyImage(`RX ${tuner}`, locked ? "Lock" : "No lock", tunerColor(tuner), {
			dim: !locked,
			frame: state.selected === tuner ? "#ffffff" : undefined
		}));
	}

	public override onKeyDown(ev: KeyDownEvent<Settings>): void {
		client.send({ cmd: "select", ...tunerArgument(ev.payload.settings.tuner ?? "1") });
	}
}

/** Mutes / unmutes the tuner (or the selected one); the key turns red while it is muted. */
@action({ UUID: "com.dh1rk.opentuner.mute" })
export class MuteTuner extends OpenTunerAction {
	protected draw(visible: AnyAction, settings: Settings): void {
		if (!visible.isKey()) {
			return;
		}

		const state = client.state;
		const tuner = resolveTuner(settings.tuner ?? "selected", state);
		if (!state || !tuner) {
			visible.setImage(keyImage("Mute", client.connected ? "no tuner" : "off", Colors.Off, { dim: true }));
			return;
		}

		const muted = state.muted[tuner - 1];
		visible.setImage(keyImage(muted ? "Muted" : "Audio", `RX ${tuner}`, muted ? Colors.Muted : tunerColor(tuner)));
	}

	public override onKeyDown(ev: KeyDownEvent<Settings>): void {
		client.send({ cmd: "mute", ...tunerArgument(ev.payload.settings.tuner ?? "selected") });
	}
}

/** Mute all: the first press mutes every tuner that is on, the next one switches on only those again. Red while all are muted. */
@action({ UUID: "com.dh1rk.opentuner.muteall" })
export class MuteAll extends OpenTunerAction {
	protected draw(visible: AnyAction): void {
		if (!visible.isKey()) {
			return;
		}

		const state = client.state;
		if (!state) {
			visible.setImage(keyImage("Mute all", client.connected ? "-" : "off", Colors.Off, { dim: true }));
			return;
		}

		visible.setImage(keyImage(state.allmuted ? "Muted" : "Mute all", state.allmuted ? "press: restore" : "all tuners", state.allmuted ? Colors.Muted : "#2e7d32"));
	}

	public override onKeyDown(): void {
		client.send({ cmd: "muteall" });
	}
}

/** Shows a tab (Properties, Expert, TS Info, Special, Chip) of the board of the tuner; the key is framed while that tab is shown. */
@action({ UUID: "com.dh1rk.opentuner.tab" })
export class ShowTab extends OpenTunerAction {
	protected draw(visible: AnyAction, settings: Settings): void {
		if (!visible.isKey()) {
			return;
		}

		const kind = settings.tab ?? "Properties";
		const state = client.state;
		const tuner = resolveTuner(settings.tuner ?? "selected", state);
		const active = !!state && (state.tab === kind || state.tab.startsWith(kind + "-"));
		// "Properties" does not fit the key at the common size
		visible.setImage(keyImage(kind === "Properties" ? "Props" : kind, tuner ? `RX ${tuner}` : "-", tuner ? tunerColor(tuner) : Colors.Off, {
			dim: !tuner,
			frame: active ? "#ffffff" : undefined
		}));
	}

	public override onKeyDown(ev: KeyDownEvent<Settings>): void {
		client.send({ cmd: "tab", tab: ev.payload.settings.tab ?? "Properties", ...tunerArgument(ev.payload.settings.tuner ?? "selected") });
	}
}

/** The view: this tuner large with the others below, only this tuner, or the grid again. */
@action({ UUID: "com.dh1rk.opentuner.view" })
export class ShowView extends OpenTunerAction {
	protected draw(visible: AnyAction, settings: Settings): void {
		if (!visible.isKey()) {
			return;
		}

		const view = settings.view ?? "large";
		const state = client.state;
		const tuner = resolveTuner(settings.tuner ?? "selected", state);
		const names: Record<string, string> = { large: "Large", only: "Only", grid: "Grid" };
		const needsTuner = view !== "grid";
		const active = !!state && state.view === view && (view === "grid" || state.large === tuner);

		visible.setImage(keyImage(names[view] ?? view, needsTuner ? (tuner ? `RX ${tuner}` : "-") : "all",
			needsTuner && tuner ? tunerColor(tuner) : Colors.Off, {
				dim: !state || (needsTuner && !tuner),
				frame: active ? "#ffffff" : undefined
			}));
	}

	public override onKeyDown(ev: KeyDownEvent<Settings>): void {
		const view = ev.payload.settings.view ?? "large";
		client.send({ cmd: "view", view, ...tunerArgument(ev.payload.settings.tuner ?? "selected") });
	}
}

/** Dial: turn = volume of the tuner (or the selected one), press = mute. The display shows tuner and level. */
@action({ UUID: "com.dh1rk.opentuner.volume" })
export class VolumeDial extends OpenTunerAction {
	protected draw(visible: AnyAction, settings: Settings): void {
		if (!visible.isDial()) {
			return;
		}

		const state = client.state;
		const tuner = resolveTuner(settings.tuner ?? "selected", state);
		if (!state || !tuner) {
			visible.setFeedback({ title: "Volume", value: client.connected ? "no tuner" : "off", indicator: { value: 0 } });
			return;
		}

		const muted = state.muted[tuner - 1];
		const volume = state.volume[tuner - 1];
		visible.setFeedback({ title: `RX ${tuner}`, value: muted ? "Muted" : `${volume} %`, indicator: { value: muted ? 0 : volume } });
	}

	public override onDialRotate(ev: DialRotateEvent<Settings>): void {
		const step = Number(ev.payload.settings.step ?? "5") || 5;
		client.send({ cmd: "volume", delta: ev.payload.ticks * step, ...tunerArgument(ev.payload.settings.tuner ?? "selected") });
	}

	public override onDialDown(ev: DialDownEvent<Settings>): void {
		client.send({ cmd: "mute", ...tunerArgument(ev.payload.settings.tuner ?? "selected") });
	}
}

export const allActions = [new SelectTuner(), new MuteTuner(), new MuteAll(), new ShowTab(), new ShowView(), new VolumeDial()];
