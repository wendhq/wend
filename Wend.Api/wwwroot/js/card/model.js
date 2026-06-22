import { api } from "../api.js";

// State + data for a single card. Re-fetches after a save so the view shows server truth.
// No DOM. Subscribers notified on change.
export function createCardModel(cardId) {
    let card = { id: cardId, listId: 0, listTitle: "", title: "", description: "", dueDate: null, position: 0 };
    const subscribers = [];
    const notify = () => subscribers.forEach((fn) => fn(card));

    return {
        subscribe(fn) {
            subscribers.push(fn);
            fn(card);
        },
        async load() {
            card = await api(`/api/cards/${cardId}`);
            notify();
        },
        async save({ title, description, dueDate }) {
            await api(`/api/cards/${cardId}`, {
                method: "PUT",
                body: JSON.stringify({ title, description, dueDate }),
            });
            await this.load();
        },
        async remove() {
            await api(`/api/cards/${cardId}`, { method: "DELETE" });
        },
    };
}