using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Tmds.DBus;
using TMPro;
using UnityEngine;

[DBusInterface("org.kde.kwin.Scripting")]
interface IScripting : IDBusObject
{
    Task<int> loadScriptAsync(string path, string name);
    Task unloadScriptAsync(string name);
}

[DBusInterface("org.kde.kwin.Script")]
interface IScriptInstance : IDBusObject
{
    Task runAsync();
}


[DBusInterface("org.kdotool.callback")]
public interface IKWinCallback : IDBusObject //Phew! This field must be public to make methods accessible to KWinCallbackReceiverAdapter!
{
    Task ResultAsync(string message);
    Task ErrorAsync(string message);
}

public class KWinCallbackReceiver : IKWinCallback
{
    public ObjectPath ObjectPath => "/";
    public List<string> Messages = new List<string>();
    public List<string> Errors = new List<string>();

    public Task ResultAsync(string message) { Messages.Add(message); return Task.CompletedTask; }
    public Task ErrorAsync(string message) { Errors.Add(message); return Task.CompletedTask; }
}

public class KWinManager : MonoBehaviour
{
    private Connection _connection;
    private ConnectionInfo _connectionInfo;
    private KWinCallbackReceiver _callbackHandler;
    private string kdeVersion;

    private async void Start()
    {
        kdeVersion = Environment.GetEnvironmentVariable("KDE_SESSION_VERSION");
        await SetupDBus();
    }

    async Task SetupDBus()
    {
        _connection = new Connection(Address.Session);
        _connectionInfo = await _connection.ConnectAsync();

        // Register our local callback object so KWin can talk to us
        _callbackHandler = new KWinCallbackReceiver();
        await _connection.RegisterObjectAsync(_callbackHandler);

        /*
        // Example usage
        var geo = await GetWindowGeometry();
        if (geo != null)
        {
            Debug.Log($"Window: {geo.Width}x{geo.Height} at {geo.X},{geo.Y}");
        }
        */
    }

    void Dispose()
    {
        _connection.UnregisterObject(_callbackHandler);
    }

    private void OnApplicationQuit()
    {
        Dispose();
    }

    void OnDestroy()
    {
        Dispose();
    }

    public async Task<WindowGeometry> GetWindowGeometry()
    {
        _callbackHandler.Messages.Clear();
        _callbackHandler.Errors.Clear();

        string scriptName = "getgeo_" + Guid.NewGuid().ToString("N");
        
        // Note: We use our connection's LocalName to tell KWin where to send the DBus call
        string jsScript = kdeVersion == "5" ? $@"
            function send(msg) {{
                callDBus(
                    '{_connectionInfo.LocalName}', 
                    '/', 
                    'org.kdotool.callback', 
                    'Result', 
                    msg
                );
            }}
            function err(msg) {{
                callDBus(
                    '{_connectionInfo.LocalName}', 
                    '/', 
                    'org.kdotool.callback', 
                    'Error', 
                    msg
                );
            }}
            
            var w = workspace.activeWindow;
            if (!w) {{
                err('NotFound');
            }} else {{
                send(w.internalId.toString());
                send(w.frameGeometry.x + ',' + w.frameGeometry.y);
                send(w.frameGeometry.width + 'x' + w.frameGeometry.height);
            }}" : $@"
            function send(msg) {{
                callDBus(
                    '{_connectionInfo.LocalName}', 
                    '/', 
                    'org.kdotool.callback', 
                    'Result', 
                    msg
                );
            }}
            function err(msg) {{
                callDBus(
                    '{_connectionInfo.LocalName}', 
                    '/', 
                    'org.kdotool.callback', 
                    'Error', 
                    msg
                );
            }}
            
            var w = workspace.activeClient;
            if (!w) {{
                err('NotFound');
            }} else {{
                send(w.internalId.toString());
                send(w.geometry.x + ',' + w.geometry.y);
                send(w.geometry.width + 'x' + w.geometry.height);
            }}";

        await ExecuteKWinScript(scriptName, jsScript);

        if (_callbackHandler.Errors.Count > 0) return null;
        if (_callbackHandler.Messages.Count < 3) return null;

        var geo = new WindowGeometry { Id = _callbackHandler.Messages[0] };
        var pos = _callbackHandler.Messages[1].Split(',');
        var size = _callbackHandler.Messages[2].Split('x');

        geo.X = int.Parse(pos[0]);
        geo.Y = int.Parse(pos[1]);
        geo.Width = int.Parse(size[0]);
        geo.Height = int.Parse(size[1]);

        return geo;
    }
    
    public async void MoveWindow(Vector2 pos)
    {
        _callbackHandler.Messages.Clear();
        _callbackHandler.Errors.Clear();

        string scriptName = "getgeo_" + Guid.NewGuid().ToString("N");
        
        // Note: We use our connection's LocalName to tell KWin where to send the DBus call
        string jsScript = kdeVersion == "5" ? $@"
            var w = workspace.activeWindow;
            w.clientStartUserMovedResized(w);
            w.frameGeometry.x += {(int)pos.x};
            w.frameGeometry.y += {(int)pos.y};
            w.clientFinishUserMovedResized(w);" : $@"
            var w = workspace.activeClient;
            w.clientStartUserMovedResized(w);
            w.geometry.x += {(int)pos.x};
            w.geometry.y += {(int)pos.y};
            w.clientFinishUserMovedResized(w);";

        await ExecuteKWinScript(scriptName, jsScript);
    }

    private async Task ExecuteKWinScript(string scriptName, string code)
    {
        string tempFile = Path.Combine(Application.temporaryCachePath, scriptName + ".js");
        await File.WriteAllTextAsync(tempFile, code);

        var scripting = _connection.CreateProxy<IScripting>("org.kde.KWin", "/Scripting");
        int scriptId = await scripting.loadScriptAsync(tempFile, scriptName);

        var instance = kdeVersion == "5" ? _connection.CreateProxy<IScriptInstance>("org.kde.KWin", $"/{scriptId}") : _connection.CreateProxy<IScriptInstance>("org.kde.KWin", $"/Scripting/Script{scriptId}");
        await instance.runAsync();
        
        // Give KWin a moment to execute and call our DBus methods
        await Task.Delay(200); 
        
        await scripting.unloadScriptAsync(scriptName);
        
        if (File.Exists(tempFile)) File.Delete(tempFile);
    }
}

public class WindowGeometry
{
    public string Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}