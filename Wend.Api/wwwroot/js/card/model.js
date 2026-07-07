import { api } from "../api.js";

// State for one card plus its board's label palette. Re-fetches after every change so the view
// always shows server truth. No DOM. Subscribers get (card, palette).
export function createCardModel(cardId) {
  let card = { id: cardId, listId: 0, listTitle: "", boardId: 0, title: "", description: "", dueDate: null, position: 0, labels: [], items: [] };
  let palette = [];
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(card, palette));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(card, palette);
    },
    async load() {
      card = await api(`/api/cards/${cardId}`);
      palette = await api(`/api/boards/${card.boardId}/labels`);
      notify();
    },
    async save({ title, description, dueDate }) {
      await api(`/api/cards/${cardId}`, { method: "PUT", body: JSON.stringify({ title, description, dueDate }) });
      await this.load();
    },
    async setDone(completed) {
      await api(`/api/cards/${cardId}/complete`, { method: "PUT", body: JSON.stringify({ completed }) });
      await this.load();
    },
    async addItem(text) {
      await api(`/api/cards/${cardId}/checklist-items`, { method: "POST", body: JSON.stringify({ text }) });
      await this.load();
    },
    async checkItem(id, checked) {
      await api(`/api/checklist-items/${id}/check`, { method: "PUT", body: JSON.stringify({ checked }) });
      await this.load();
    },
    async renameItem(id, text) {
      await api(`/api/checklist-items/${id}`, { method: "PUT", body: JSON.stringify({ text }) });
      await this.load();
    },
    async moveItem(id, position) {
      await api(`/api/checklist-items/${id}/move`, { method: "PUT", body: JSON.stringify({ position }) });
      await this.load();
    },
    async remove() {
      await api(`/api/cards/${cardId}`, { method: "DELETE" });
    },
    async attachLabel(labelId) {
      await api(`/api/cards/${cardId}/labels`, { method: "POST", body: JSON.stringify({ labelId }) });
      await this.load();
    },
    async detachLabel(labelId) {
      await api(`/api/cards/${cardId}/labels/${labelId}`, { method: "DELETE" });
      await this.load();
    },
    async createLabel(name, colour) {
      const label = await api(`/api/boards/${card.boardId}/labels`, { method: "POST", body: JSON.stringify({ name, colour }) });
      await api(`/api/cards/${cardId}/labels`, { method: "POST", body: JSON.stringify({ labelId: label.id }) }); // auto-attach
      await this.load();
    },
    async editLabel(id, name, colour) {
      await api(`/api/labels/${id}`, { method: "PUT", body: JSON.stringify({ name, colour }) });
      await this.load();
    },
    async deleteLabel(id) {
      await api(`/api/labels/${id}`, { method: "DELETE" });
      await this.load();
    },
  };
}
