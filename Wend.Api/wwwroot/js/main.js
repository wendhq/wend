import { createBoardsModel } from "./boards/model.js";
import { createBoardsView } from "./boards/view.js";
import { createBoardsController } from "./boards/controller.js";
import { createAnnouncer } from "./announce.js";

const announce = createAnnouncer(document.getElementById("status"));
const model = createBoardsModel();
const view = createBoardsView(document.getElementById("app"));
createBoardsController(model, view, announce);
model.load();
