import WebSocket from "ws";

/** The state OpenTuner sends (see MainForm.StreamDeck.cs). "selected" and "large" are 1 based, 0 = none. */
export type OpenTunerState = {
	type: "state";
	tuners: number;
	selected: number;
	view: "grid" | "large" | "only";
	large: number;
	tab: string;
	muted: boolean[];
	volume: number[];
	locked: boolean[];
};

export const DefaultPort = 8765;
const ReconnectMs = 3000;

/** Connection to the Stream Deck server of OpenTuner (127.0.0.1 only). Reconnects by itself while OpenTuner is not running. */
class OpenTunerClient {
	public state: OpenTunerState | null = null;

	private port = DefaultPort;
	private socket: WebSocket | null = null;
	private timer: NodeJS.Timeout | null = null;
	private readonly listeners = new Set<() => void>();

	public get connected(): boolean {
		return this.socket?.readyState === WebSocket.OPEN;
	}

	/** Called whenever the state or the connection changed. */
	public onChange(listener: () => void): void {
		this.listeners.add(listener);
	}

	public setPort(port: number): void {
		if (port === this.port && this.socket) {
			return;
		}

		this.port = port;
		this.socket?.close();
		this.connect();
	}

	public connect(): void {
		if (this.timer) {
			clearTimeout(this.timer);
			this.timer = null;
		}

		const socket = new WebSocket(`ws://127.0.0.1:${this.port}/`);
		this.socket = socket;

		socket.on("open", () => this.notify());
		socket.on("message", (data) => {
			try {
				const message = JSON.parse(data.toString());
				if (message.type === "state") {
					this.state = message as OpenTunerState;
					this.notify();
				}
			} catch {
				// not our JSON: ignored
			}
		});
		socket.on("error", () => {
			// 'close' follows, the reconnect is done there
		});
		socket.on("close", () => {
			if (this.socket !== socket) {
				return;   // replaced by a newer connection (port changed)
			}

			this.socket = null;
			this.state = null;
			this.notify();
			this.timer = setTimeout(() => this.connect(), ReconnectMs);
		});
	}

	/** "tuner" is 1 based, left out = the tuner selected in OpenTuner. */
	public send(command: Record<string, unknown>): void {
		if (this.connected) {
			this.socket?.send(JSON.stringify(command));
		}
	}

	private notify(): void {
		this.listeners.forEach((listener) => listener());
	}
}

export const client = new OpenTunerClient();
