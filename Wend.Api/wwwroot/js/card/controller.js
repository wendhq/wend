// Wires the task view to the model: save, delete, and the full label picker (attach / detach /
// create / edit / delete). Announces each result and restores focus after server round-trips.
// onBack() returns to the board (focusing this card); onDeleted() returns to the board.
export function createCardController(model, view, announce, {onBack, onDeleted} = {}) {
    let palette = [];
    let current = null;
    const nameOf = (id) => (palette.find((l) => l.id === id) || {}).name || "the label";

    view.bindActions({
        back: () => onBack?.(),
        save: async ({title, description, dueDate}) => {
            if (!title) return;
            try {
                await model.save({title, description, dueDate});
                announce("Card saved.");
            } catch {
                announce("Couldn't save the card — please try again.");
            }
        },
        delete: async () => {
            try {
                await model.remove();
                onDeleted?.(current.id, current.title);
            } catch {
                announce("Couldn't delete the card — please try again.");
            }
        },
        toggleDone: async (completed) => {
            try {
                await model.setDone(completed);
                announce(completed ? "Marked done." : "Restored.");
                view.focusDoneToggle();
            } catch {
                announce("Couldn't update the card — please try again.");
            }
        },
        attachLabel: async (id) => {
            try {
                await model.attachLabel(id);
                announce(`Added label ${nameOf(id)}.`);
                view.focusToggle(id);
            } catch {
                announce("Couldn't add the label — please try again.");
            }
        },
        detachLabel: async (id) => {
            try {
                await model.detachLabel(id);
                announce(`Removed label ${nameOf(id)}.`);
                view.focusToggle(id);
            } catch {
                announce("Couldn't remove the label — please try again.");
            }
        },
        createLabel: async (name, colour) => {
            try {
                await model.createLabel(name, colour);
                announce(`Created label ${name}.`);
            } catch {
                announce("Couldn't create the label — please try again.");
            }
        },
        editLabel: async (id, name, colour) => {
            try {
                await model.editLabel(id, name, colour);
                announce(`Updated label ${name}.`);
            } catch {
                announce("Couldn't update the label — please try again.");
            }
        },
        deleteLabel: async (id, name) => {
            if (!confirm(`Delete
            '${name}'? It will be removed from every card that uses it.`)) return;
            try {
                await model.deleteLabel(id);
                announce("Label deleted.");
                view.focusPicker();
            } catch {
                announce("Couldn't delete the label — please try again.");
            }
        },
    });

    model.subscribe((card, p) => {
        current = card;
        palette = p ?? [];
        view.render(card, p);
    });
}
