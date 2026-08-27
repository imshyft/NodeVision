const { app, BrowserWindow, screen, ipcMain, dialog } = require('electron');
const fs = require('fs').promises
const path = require('path');
const isDev = !app.isPackaged;

function createWindow() {
    const primaryDisplay = screen.getPrimaryDisplay();
    const { width, height } = primaryDisplay.workAreaSize; 

    const win = new BrowserWindow({
        width, height,
        webPreferences: {
            nodeIntegration: false,
            contextIsolation: true,
            preload: path.join(__dirname, 'preload.js')
        }
    });
    
    if (isDev) {
        win.loadURL('http://localhost:5173');
    } else {
        win.loadFile(path.join(__dirname, 'dist/index.html'));
    }
}

app.whenReady().then(createWindow);

app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') app.quit();
});

ipcMain.handle('save-file', async (event, {data, filename, extension = 'json'}) => {
    const win = BrowserWindow.getFocusedWindow();

    const { canceled, filePath } = await dialog.showSaveDialog(win, {
        title: "Export File",
        defaultPath: filename,
        filters: [
            {
                name: `${extension.toUpperCase()} Files`, 
                extensions: [extension]
            },
            {
                name: 'All Files', 
                extensions: ['*']
            }
        ]
    })

    if (canceled || !filePath) {
        return {
            success: false,
            message: "Cancelled by User"
        }
    }

    try {
        await fs.writeFile(filePath, data, 'utf-8');
        return {
            success: true,
            filePath
        };
    } catch (err) {
        return {
            success: false,
            error: err.message
        }
    }
});

ipcMain.handle('load-file', async (event, {extensions = ['json']} = {}) => {
    const win = BrowserWindow.getFocusedWindow();

    const { cancelled, filePaths } = await dialog.showOpenDialog(win, {
        title: "Open File",
        properties: ['openFile'],
        filters: [
            {
                name: 'Supported Files', 
                extensions: extensions
            },
            {
                name: 'All files', 
                extensions: ['*']
            }
        ]
    })

    if (cancelled || !filePaths.length === 0) {
        return {
            success: false,
            message: 'Cancelled by User'
        }
    }

    const selectedPath = filePaths[0];

    try {
        const fileData = await fs.readFile(selectedPath, 'utf-8');
        return {
            success: true,
            data: fileData,
            filePath: selectedPath
        };
    } catch (err) {
        return {
            success: false,
            error: err.message
        }
    }

}) 