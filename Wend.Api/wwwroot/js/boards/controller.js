// Wires board actions to the model, announces results, returns focus, surfaces failures.
// onOpen(boardId) is called when a board is opened — main.js navigates to its view.
export function createBoardsController(model, view, announce, { onOpen } = {}) {
  view.bindActions({
    open: (id) => onOpen?.(id),
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
    rename: async (id, text) => {
      try {
        await model.rename(id, text);
        announce("Board renamed.");
        view.focusRenameTrigger(id);
      } catch {
        announce("Couldn't rename the board — please try again.");
      }
    },
    delete: async (id) => {
      // Deleting a whole board is a big destructive action → confirm (per spec).
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
