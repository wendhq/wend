// Wires the board view to the model: announces results, manages focus, confirms list deletes,
// turns move-left/right into a target position, and forwards card actions (reorder + move-to-list).
// onBack() returns to the overview; onOpenCard(cardId) opens a card's task view.
export function createBoardController(model, view, announce, { onBack, onOpenCard } = {}) {
    let lists = [];

    view.bindActions({
        back: () => onBack?.(),
        openCard: (cardId) => onOpenCard?.(cardId),
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
        createCard: async (listId, title) => {
            if (!title) return;
            try {
                await model.createCard(listId, title);
                announce("Card added.");
                view.focusNewCardInput(listId);
            } catch {
                announce("Couldn't add the card — please try again.");
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
        cardUp: (cardId) => moveCard(cardId, -1, "card-up"),
        cardDown: (cardId) => moveCard(cardId, +1, "card-down"),
        moveCardTo: async (cardId, listId) => {
            const dest = lists.find((l) => l.id === listId);
            if (!dest) return;
            try {
                // Append at the bottom of the chosen list (server clamps to its length).
                await model.moveCard(cardId, listId, (dest.cards ?? []).length);
                announce(`Card moved to ${dest.title}.`);
                view.focusCardAction(cardId, "card-up");
            } catch {
                announce("Couldn't move the card — please try again.");
            }
        },
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

    async function moveCard(cardId, delta, action) {
        const list = lists.find((l) => (l.cards ?? []).some((c) => c.id === cardId));
        if (!list) return;
        const cards = list.cards ?? [];
        const index = cards.findIndex((c) => c.id === cardId);
        const target = index + delta;
        if (target < 0 || target >= cards.length) return; // already at an end (button disabled)
        try {
            await model.moveCard(cardId, list.id, target);
            announce(delta < 0 ? "Card moved up." : "Card moved down.");
            view.focusCardAction(cardId, action);
        } catch {
            announce("Couldn't move the card — please try again.");
        }
    }

    model.subscribe((board) => {
        lists = board.lists;
        view.render(board);
    });
}
