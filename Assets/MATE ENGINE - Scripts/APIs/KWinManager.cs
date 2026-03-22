using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Tmds.DBus;
using UnityEngine;
using Debug = UnityEngine.Debug;

[DBusInterface("org.kde.kwin.Scripting")]
internal interface IScripting : IDBusObject
{
    Task<int> loadScriptAsync(string path, string name);
    Task unloadScriptAsync(string name);
}

[DBusInterface("org.kde.kwin.Script")]
internal interface IScriptInstance : IDBusObject
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
    public List<string> Messages = new();
    public List<string> Errors = new();

    public Task ResultAsync(string message) { Messages.Add(message); return Task.CompletedTask; }
    public Task ErrorAsync(string message) { Errors.Add(message); Debug.LogError(message); return Task.CompletedTask; }
}

public class KWinManager : Singleton<KWinManager>
{
    private Connection _connection;
    private ConnectionInfo _connectionInfo;
    private KWinCallbackReceiver _callbackHandler;
    private string _kdeVersion;
    private string _windowUuid;

    public string UnityWindow => _windowUuid;

    private IScripting _scripting;

    private string _template;

    private new async void Awake()
    {
        try
        {
            base.Awake();
            _kdeVersion = Environment.GetEnvironmentVariable("KDE_SESSION_VERSION");
            await SetupDBus();
            _windowUuid = await GetSelfWindowUuid();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private void Dispose()
    {
        if (_cachedScriptPaths.Count > 0)
        {
            foreach (var path in _cachedScriptPaths)
            {
                File.Delete(path);
            }
        }
        _cachedScriptPaths.Clear();
        _connection.UnregisterObject(_callbackHandler);
    }

    private void OnApplicationQuit()
    {
        Dispose();
    }

    private void OnDestroy()
    {
        Dispose();
    }

    private async Task SetupDBus()
    {
        _connection = new Connection(Address.Session);
        _connectionInfo = await _connection.ConnectAsync();

        // Register our local callback object so KWin can talk to us
        _callbackHandler = new KWinCallbackReceiver();
        await _connection.RegisterObjectAsync(_callbackHandler);
        _scripting = _connection.CreateProxy<IScripting>("org.kde.KWin", "/Scripting");

        _template = $@"
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
            }}";
    }

    private void ClearHandlerMessages()
    {
        _callbackHandler.Messages.Clear();
        _callbackHandler.Errors.Clear();
    }

    private async Task<string> GetSelfWindowUuid()
    {
        string scriptName = "KWin_GetWinUUID.js";
        string jsScript = _template + $@"
            for (let win of workspace.{(_kdeVersion.StartsWith("5") ? "clientList" : "windowList")}()) {{
                if (win.pid == {Process.GetCurrentProcess().Id}) {{
                    send(win.internalId.toString());
                    break;
                }}
            }}";
        await File.WriteAllTextAsync(Path.Combine(Application.temporaryCachePath, scriptName), jsScript);
        await ExecuteKWinScript(scriptName, true, true);

        return _callbackHandler.Messages[0];
    }
    
    public async Task<int> GetWindowPid(string uuid = null)
    {
        if (string.IsNullOrEmpty(uuid)) uuid = _windowUuid;
        
        string scriptName = "KWin_GetWinPID.js";
        string jsScript = _template + $@"
            for (let win of workspace.{(_kdeVersion.StartsWith("5") ? "clientList" : "windowList")}()) {{
                if (win.internalId.toString() == ""{uuid}"") {{
                    send(win.pid.toString());
                    break;
                }}
            }}";
        await File.WriteAllTextAsync(Path.Combine(Application.temporaryCachePath, scriptName), jsScript);
        await ExecuteKWinScript(scriptName, false, true);

        int.TryParse(_callbackHandler.Messages[0], out var result);
        return result;
    }
    
    public async Task<List<string>> GetAllWindows()
    {
        string scriptName = "KWin_GetAllWin.js";
        string scriptPath = Path.Combine(Application.temporaryCachePath, scriptName);
        string jsScript = _template + $@"
            for (let win of workspace.{(_kdeVersion.StartsWith("5") ? "clientList" : "windowList")}()) {{
                send(win.internalId.toString());
            }}";
        
        if (!File.Exists(scriptPath)) await File.WriteAllTextAsync(scriptPath, jsScript);
        await ExecuteKWinScript(scriptName, false, true);

        return _callbackHandler.Messages;
    }

    public async Task<RectInt> GetWindowGeometry(string uuid = null)
    {
        if (string.IsNullOrEmpty(uuid)) uuid = _windowUuid;
        
        string scriptName = $"KWin_GetGeometryFor{uuid.Replace("-", "_")}.js";
        string scriptPath = Path.Combine(Application.temporaryCachePath, scriptName);
        string jsScript = _template + $@"
            for (let w of workspace.{(_kdeVersion.StartsWith("5") ? "clientList" : "windowList")}()) {{
                if (w.internalId.toString() == ""{uuid}"") {{
                    send(w.frameGeometry.x + ',' + w.frameGeometry.y);
                    send(w.frameGeometry.width + 'x' + w.frameGeometry.height);
                    break;
                }}
            }}";

        if (!File.Exists(scriptPath)) await File.WriteAllTextAsync(scriptPath, jsScript);

        await ExecuteKWinScript(scriptName, true, true);

        var geo = new RectInt();
        var pos = _callbackHandler.Messages[0].Split(',');
        var size = _callbackHandler.Messages[1].Split('x');

        geo.x = int.Parse(pos[0]);
        geo.y = int.Parse(pos[1]);
        geo.width = int.Parse(size[0]);
        geo.height = int.Parse(size[1]);

        return geo;
    }
    
    public async Task<Vector2Int> GetCursorPos()
    {
        string scriptName = "KWin_GetCursorPos.js";
        string scriptPath = Path.Combine(Application.temporaryCachePath, scriptName);
        string jsScript = _template + $@"
                send(workspace.cursorPos.x + ',' + workspace.cursorPos.y);
            }}";
        
        if (!File.Exists(scriptPath)) await File.WriteAllTextAsync(scriptPath, jsScript);
        await ExecuteKWinScript(scriptName, false, true);

        var pos = _callbackHandler.Messages[0].Split(',');

        return new Vector2Int(int.Parse(pos[0]), int.Parse(pos[1]));
    }
    
    public async Task MoveWindow(Vector2 pos)
    {
        string scriptName = $"KWin_MoveWin.js"; 
        string scriptPath = Path.Combine(Application.temporaryCachePath, scriptName);

        string jsScript = $@"
            for (let w of workspace.{(_kdeVersion.StartsWith("5") ? "clientList" : "windowList")}()) {{
                if (w.internalId.toString() == ""{_windowUuid}"") {{
                    w.clientStartUserMovedResized(w);
                    w.geometry.x = {(int)pos.x};
                    w.geometry.y = {(int)pos.y};
                    w.clientFinishUserMovedResized(w);
                    break;
                }}
            }}";
        
        await File.WriteAllTextAsync(scriptPath, jsScript);

        await ExecuteKWinScript(scriptName, false, false);
    }

    private readonly List<string> _cachedScriptPaths = new();
    
    private async Task ExecuteKWinScript(string scriptFileName, bool deleteOnFinishExecution, bool throwOnEmptyOutput)
    {
        ClearHandlerMessages();
        
        string scriptPath = Path.Combine(Application.temporaryCachePath, scriptFileName);

        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"Attempting to execute script {scriptFileName} while it's not found under {Path.GetDirectoryName(scriptPath)}.");
        
        int scriptId = await _scripting.loadScriptAsync(scriptPath, Path.GetFileNameWithoutExtension(scriptFileName));

        var instance = _kdeVersion == "5" ? _connection.CreateProxy<IScriptInstance>("org.kde.KWin", $"/{scriptId}") : _connection.CreateProxy<IScriptInstance>("org.kde.KWin", $"/Scripting/Script{scriptId}");
        
        await instance.runAsync();
        
        await _scripting.unloadScriptAsync(Path.GetFileNameWithoutExtension(scriptFileName));
        
        if (_callbackHandler.Errors.Count > 0)
            throw new ($"Errors during execution of script {scriptFileName}.");
        if (_callbackHandler.Messages.Count < 1 && throwOnEmptyOutput)
            throw new($"Empty output of script {scriptFileName}. Please check if there are script errors in journal.");

        if (!deleteOnFinishExecution)
        {
            _cachedScriptPaths.Add(scriptPath);
            return;
        }
        
        if (File.Exists(scriptPath)) File.Delete(scriptPath);
    }
}