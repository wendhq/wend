// Wires the task view to the model: announces results, surfaces failures, navigates on
// delete. Delete is immediate in Plan 3 (no confirm; undo arrives in Plan 7).
// onBack() returns to the board (focusing this card); onDeleted() returns to the board.
export function createCardController(model, view, announce, { onBack, onDeleted } = {}) {
    view.bindActions({
        back: () => onBack?.(),
        save: async ({ title, description, dueDate }) => {
            if (!title) return;
            try {
                await model.save({ title, description, dueDate });
                announce("Card saved.");
            } catch {
                announce("Couldn't save the card — please try again.");
            }
        },
        delete: async () => {
            try {
                await model.remove();
                announce("Card deleted.");
                onDeleted?.();
            } catch {
                announce("Couldn't delete the card — please try again.");
            }
        },
    });

    model.subscribe((card) => view.render(card));
}