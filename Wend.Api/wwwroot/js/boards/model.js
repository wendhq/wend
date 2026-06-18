import { api } from "../api.js";

export function createBoardsModel() {
  let boards = [];
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(boards));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(boards);
    },
    async load() {
      boards = await api("/api/boards");
      notify();
    },
    async create(title) {
      await api("/api/boards", { method: "POST", body: JSON.stringify({ title }) });
      await this.load();
    },
    async rename(id, title) {
      await api(`/api/boards/${id}`, { method: "PUT", body: JSON.stringify({ title }) });
      await this.load();
    },
    async remove(id) {
      await api(`/api/boards/${id}`, { method: "DELETE" });
      await this.load();
    },
  };
}
