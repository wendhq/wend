// Wires the task view to the model: save, delete, and the full label picker (attach / detach /
// create / edit / delete). Announces each result and restores focus after server round-trips.
// onBack() returns to the board (focusing this card); onDeleted() returns to the board.
export function createCardController(model, view, announce, {onBack, onDeleted} = {}) {
    let palette = [];
    let current = null;
    const nameOf = (id) => (palette.find((l) => l.id === id) || {}).name || "the label";

    async function moveItem(id, delta, action) {
        const unchecked = (current.items ?? []).filter((i) => !i.checkedAt);
        const index = unchecked.findIndex((i) => i.id === id);
        if (index < 0) return;
        const target = index + delta;
        if (target < 0 || target >= unchecked.length) return; // already at an end (button is disabled)
        const text = unchecked[index].text;
        try {
            await model.moveItem(id, unchecked[target].position);
            announce(`${delta < 0 ? "Moved up" : "Moved down"}: ${text}.`);
            view.focusItemAction(id, action);
        } catch {
            announce("Couldn't move the item — please try again.");
        }
    }

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
        addItem: async (text) => {
            try {
                await model.addItem(text);
                announce("Item added.");
                view.focusAddInput(); // consecutive adds flow without re-tabbing
            } catch {
                announce("Couldn't add the item — please try again.");
            }
        },
        toggleItem: async (id, checked) => {
            const item = (current.items ?? []).find((i) => i.id === id);
            const text = item ? item.text : "the item";
            try {
                await model.checkItem(id, checked);
                const items = current.items ?? [];
                const doneCount = items.filter((i) => i.checkedAt).length;
                announce(`${checked ? "Checked" : "Un-checked"}: ${text} — ${doneCount} of ${items.length} done.`);
                if (checked) view.focusDoneStripToggle(); // the row left for the (maybe collapsed) strip
                else view.focusItem(id);                  // the row is back among the unchecked
            } catch {
                announce("Couldn't update the item — please try again.");
            }
        },
        renameItem: async (id, text) => {
            try {
                await model.renameItem(id, text);
                announce("Item renamed.");
                view.focusRenameTrigger(id);
            } catch {
                announce("Couldn't rename the item — please try again.");
            }
        },
        toggleEditMode: () => {
            const on = view.toggleEditMode();
            announce(on ? "Edit mode on." : "Edit mode off.");
        },
        saveTitle: async (text) => {
            try {
                await model.save({ title: text, description: current.description, dueDate: current.dueDate });
                announce("Card renamed.");
                view.focusTitleTrigger();
            } catch {
                announce("Couldn't rename the card — please try again.");
            }
        },
        moveItemUp: (id) => moveItem(id, -1, "item-up"),
        moveItemDown: (id) => moveItem(id, +1, "item-down"),
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
