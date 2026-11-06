import WebSocket from "ws";

/** The state OpenTuner sends (see MainForm.StreamDeck.cs). "selected" and "large" are 1 based, 0 = none. */
export type OpenTunerState = {
	type: "state";
	tuners: number;
	selected: number;
	view: "grid" | "large" | "only";
	large: number;
	tab: string;
	/** true if no tuner is on */
	allmuted: boolean;
	muted: boolean[];
	volume: number[];
	locked: boolean[];
};

export const DefaultPort = 8765;
const ReconnectMs = 3000;

/** Connection to the Stream Deck server of OpenTuner (127.0.0.1 only). Reconnects by itself while OpenTuner is not running. */
export class OpenTunerClient {
	public state: OpenTunerState | null = null;

	private socket: WebSocket | null = null;
	private timer: NodeJS.Timeout | null = null;
	private readonly listeners = new Set<() => void>();

	public constructor(public readonly port: number) {}

	public get connected(): boolean {
		return this.socket?.readyState === WebSocket.OPEN;
	}

	/** Called whenever the state or the connection changed. */
	public onChange(listener: () => void): void {
		this.listeners.add(listener);
	}

	/** Closes the connection for good (no reconnect): nothing uses this port any more. */
	public close(): void {
		if (this.timer) {
			clearTimeout(this.timer);
			this.timer = null;
		}

		const socket = this.socket;
		this.socket = null;
		this.state = null;
		socket?.close();
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
				return;   // closed or replaced by a newer connection
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

/** "8766" gives 8766; empty or not a usable port gives the default port 8765. */
export function portOf(setting: string | undefined): number {
	const port = Number(setting);
	return port >= 1024 && port <= 65535 ? port : DefaultPort;
}

const clients = new Map<number, OpenTunerClient>();
const changeListeners = new Set<() => void>();

/** The connection to the OpenTuner instance on this port: made when a key needs it for the first time, then kept. */
export function clientFor(port: number): OpenTunerClient {
	let client = clients.get(port);
	if (!client) {
		client = new OpenTunerClient(port);
		client.onChange(() => changeListeners.forEach((listener) => listener()));
		clients.set(port, client);
		client.connect();
	}

	return client;
}

/** Called whenever the state or the connection of any instance changed. */
export function onAnyChange(listener: () => void): void {
	changeListeners.add(listener);
}

/** Closes the connections of the ports that no key uses any more. */
export function closeUnused(usedPorts: Set<number>): void {
	for (const [port, client] of clients) {
		if (!usedPorts.has(port)) {
			client.close();
			clients.delete(port);
		}
	}
}
