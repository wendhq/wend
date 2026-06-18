export function createBoardsController(model, view, announce) {
  view.bindActions({
    create: async (title) => {
      if (!title) return;
      try {
        await model.create(title);
        announce("Board added.");
        view.focusNewBoardInput();
      } catch {
        announce("Couldn't add the board — please try again.");
      }
    },
    rename: async (id) => {
      const title = prompt("New board name?");
      if (!title || !title.trim()) return;
      try {
        await model.rename(id, title.trim());
        announce("Board renamed.");
        view.focusNewBoardInput();
      } catch {
        announce("Couldn't rename the board — please try again.");
      }
    },
    delete: async (id) => {
      if (!confirm("Delete this board and everything in it?")) return;
      try {
        await model.remove(id);
        announce("Board deleted.");
        view.focusNewBoardInput();
      } catch {
        announce("Couldn't delete the board — please try again.");
      }
    },
  });
  model.subscribe((boards) => view.render(boards));
}
