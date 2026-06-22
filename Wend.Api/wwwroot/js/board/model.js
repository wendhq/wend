import { api } from "../api.js";

// State + data for a single board's lists. Re-fetches the board detail after each change
// so positions always come straight from the server. No DOM. Subscribers notified on change.
export function createBoardModel(boardId) {
    let board = { id: boardId, title: "", lists: [] };
    const subscribers = [];
    const notify = () => subscribers.forEach((fn) => fn(board));

    return {
        subscribe(fn) {
            subscribers.push(fn);
            fn(board);
        },
        async load() {
            board = await api(`/api/boards/${boardId}`);
            notify();
        },
        async create(title) {
            await api(`/api/boards/${boardId}/lists`, { method: "POST", body: JSON.stringify({ title }) });
            await this.load();
        },
        async rename(id, title) {
            await api(`/api/lists/${id}`, { method: "PUT", body: JSON.stringify({ title }) });
            await this.load();
        },
        async remove(id) {
            await api(`/api/lists/${id}`, { method: "DELETE" });
            await this.load();
        },
        async move(id, position) {
            await api(`/api/lists/${id}/move`, { method: "PUT", body: JSON.stringify({ position }) });
            await this.load();
        },
        async createCard(listId, title) {
            await api(`/api/lists/${listId}/cards`, { method: "POST", body: JSON.stringify({ title }) });
            await this.load();
        },
    };
}
