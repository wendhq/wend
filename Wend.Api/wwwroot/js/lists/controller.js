// Wires the board view to the model: announces results, manages focus, confirms deletes,
// and turns move-left/right into a target position. onBack() returns to the overview.
export function createListsController(model, view, announce, { onBack } = {}) {
    let lists = [];

    view.bindActions({
        back: () => onBack?.(),
        create: async (title) => {
            if (!title) return;
            try {
                await model.create(title);
                announce("List added.");
                view.focusNewListInput();
            } catch {
                announce("Couldn't add the list — please try again.");
            }
        },
        rename: async (id) => {
            const title = prompt("New list name?");
            if (!title || !title.trim()) return;
            try {
                await model.rename(id, title.trim());
                announce("List renamed.");
                view.focusNewListInput();
            } catch {
                announce("Couldn't rename the list — please try again.");
            }
        },
        delete: async (id) => {
            if (!confirm("Delete this list?")) return;
            try {
                await model.remove(id);
                announce("List deleted.");
                view.focusNewListInput();
            } catch {
                announce("Couldn't delete the list — please try again.");
            }
        },
        moveLeft: (id) => move(id, -1, "move-left"),
        moveRight: (id) => move(id, +1, "move-right"),
    });

    async function move(id, delta, action) {
        const index = lists.findIndex((l) => l.id === id);
        if (index < 0) return;
        const target = index + delta;
        if (target < 0 || target >= lists.length) return; // already at an end (button is disabled)
        try {
            await model.move(id, target);
            announce(delta < 0 ? "List moved left." : "List moved right.");
            view.focusListAction(id, action);
        } catch {
            announce("Couldn't move the list — please try again.");
        }
    }

    model.subscribe((board) => {
        lists = board.lists;
        view.render(board);
    });
}