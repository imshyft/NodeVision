const { app, BrowserWindow, screen } = require('electron');
const path = require('path');
const isDev = !app.isPackaged;

function createWindow() {
    const primaryDisplay = screen.getPrimaryDisplay();
    const { width, height } = primaryDisplay.workAreaSize; 

    const win = new BrowserWindow({
        width, height,
        webPreferences: {
            nodeIntegration: false,
            contextIsolation: true
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