"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.deactivate = exports.activate = void 0;
// The module 'vscode' contains the VS Code extensibility API
// Import the module and reference it with the alias vscode in your code below
const vscode = require("vscode");
// This method is called when your extension is activated
// Your extension is activated the very first time the command is executed
function activate(context) {
    const disposable = vscode.debug.registerDebugAdapterDescriptorFactory("topgun", new TopGunDebugAdapterDescriptorFactory());
    context.subscriptions.push(disposable);
}
exports.activate = activate;
// This method is called when your extension is deactivated
function deactivate() { }
exports.deactivate = deactivate;
class TopGunDebugAdapterDescriptorFactory {
    createDebugAdapterDescriptor(session, executable) {
        const command = session.configuration["debugAdapter"] ||
            "C:\\dev\\TopGun\\TopGunDebugAdapter\\bin\\Debug\\net7.0\\TopGunDebugAdapter.exe";
        const args = [
            "--resourceDir", session.configuration["resourceDir"] || session.workspaceFolder?.uri.fsPath,
            "--engineHost", session.configuration["engineHost"] || "127.0.0.1",
            "--enginePort", session.configuration["enginePort"] || "2346",
            "--stopOnEntry", "stopOnEntry" in session.configuration ? !!session.configuration["stopOnEntry"] : true,
            "--waitForDebugger", !!session.configuration["waitForDotnetDebugger"],
            "--verbose", !!session.configuration["verboseDebugAdapter"]
        ];
        return executable = new vscode.DebugAdapterExecutable(command, args);
    }
}
//# sourceMappingURL=extension.js.map