// The module 'vscode' contains the VS Code extensibility API
// Import the module and reference it with the alias vscode in your code below
import * as vscode from 'vscode';

// This method is called when your extension is activated
// Your extension is activated the very first time the command is executed
export function activate(context: vscode.ExtensionContext) {
    const disposable = vscode.debug.registerDebugAdapterDescriptorFactory("topgun", new TopGunDebugAdapterDescriptorFactory());
    context.subscriptions.push(disposable);
}

// This method is called when your extension is deactivated
export function deactivate() {}

class TopGunDebugAdapterDescriptorFactory implements vscode.DebugAdapterDescriptorFactory {
    createDebugAdapterDescriptor(session: vscode.DebugSession, executable: vscode.DebugAdapterExecutable | undefined): vscode.ProviderResult<vscode.DebugAdapterDescriptor> {
        const command = session.configuration["debugAdapter"] ||
            "C:\\dev\\TopGun\\TopGunDebugAdapter\\bin\\Debug\\net7.0\\TopGunDebugAdapter.exe";

        const args = [
            "--resourceDir", session.configuration["resourceDir"] || session.workspaceFolder?.uri.fsPath,
            "--engineHost", session.configuration["engineHost"] || "127.0.0.1",
            "--enginePort", session.configuration["enginePort"] || "2346",
            "--mergeRootCalcFrames", "mergeRootCalcFrames" in session.configuration ? !!session.configuration["mergeRootCalcFrames"] : true,
            "--stopOnEntry", "stopOnEntry" in session.configuration ? !!session.configuration["stopOnEntry"] : true,
            "--waitForDebugger", !!session.configuration["waitForDotnetDebugger"],
            "--verbose", !!session.configuration["verboseDebugAdapter"]
        ];

        return executable = new vscode.DebugAdapterExecutable(command, args);
    }

}
