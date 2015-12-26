using System;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

public class MapTile
{
    internal readonly int X;
    internal readonly int Y;
    internal readonly int Zoom;

    internal Texture2D Tex;
    internal volatile byte[] Data;
    internal volatile bool ToBeRendered;

    internal MapTile(int x, int y, int zoom)
    {
        X = x;
        Y = y;
        Zoom = zoom;

        // loading texture
        Tex = new Texture2D(1, 1, TextureFormat.RGB24, false);
        Tex.SetPixel(0, 0, new Color(252, 251, 231));
        Tex.Apply();

        ToBeRendered = false;
    }

    internal void SetData(byte[] data)
    {
        Data = data;
        ToBeRendered = true;
    }

    internal void RenderData()
    {
        Tex = new Texture2D(256, 256, TextureFormat.RGB24, false);
        Tex.LoadImage(Data);
        ToBeRendered = false;
    }

    public override bool Equals(object obj)
    {
        var tile = obj as MapTile;
        if (tile == null) return false;
        return tile.X == X && tile.Y == Y && tile.Zoom == Zoom;
    }

    protected bool Equals(MapTile other)
    {
        return X == other.X && Y == other.Y && Zoom == other.Zoom;
    }

    public bool Equals(int x, int y, int zoom)
    {
        return (X == x) && (Y == y) && (Zoom == zoom);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = X;
            hashCode = (hashCode*397) ^ Y;
            hashCode = (hashCode*397) ^ Zoom;
            return hashCode;
        }
    }
}

public static class Extensions
{
    public static bool TileExists(this List<MapTile> list, int x, int y, int zoom)
    {
        return list.Exists(item => item.X == x && item.Y == y && item.Zoom == zoom);
    }

    public static MapTile GetTile(this List<MapTile> list, int x, int y, int zoom)
    {
        return list.Find(item => item.X == x && item.Y == y && item.Zoom == zoom);
    }
}

public class MapHandler : MonoBehaviour
{
    private const int MaxThreads = 25;

    private const int Zoom = 16;
    private const float Lat = 47.9874f;
    private const float Long = 7.8945f;

    private const float MovSpeed = 0.8f;
    private const float Damping = 0.92f;

    private volatile MapTile[] _mapTiles;
    private volatile List<MapTile> _tileCache;

    private int _activeThreads;
    private volatile bool _dlHandlerActive;
    private volatile List<MapTile> _downloadList;

    private int _noXTiles;
    private int _noYTiles;

    private int test = 0;

    private Vector2 _targetTile;
    private Vector3 _mousePos;
    private Vector2 _mouseVel;

    public Vector2 WorldToTilePos(double lat, double lon, int zoom)
    {
        var x = (float)((lon + 180.0) / 360.0 * (1 << zoom));
        var y = (float)((1.0 - Math.Log(Math.Tan(lat * Math.PI / 180.0) +
                                        1.0 / Math.Cos(lat * Math.PI / 180.0)) / Math.PI) / 2.0 * (1 << zoom));

        return new Vector2(x, y);
    }

    private void Start()
    {
        _noXTiles = (int) Math.Ceiling(Screen.width/256f);
        _noXTiles = _noXTiles + (_noXTiles - 1)%2 + 2;

        _noYTiles = (int) Math.Ceiling(Screen.height/256f);
        _noYTiles = _noYTiles + (_noYTiles - 1)%2 + 2;

        _tileCache = new List<MapTile>();
        _mapTiles = new MapTile[_noXTiles*_noYTiles];

        var targetVec = new Vector2(Lat, Long);
        _targetTile = WorldToTilePos(targetVec.x, targetVec.y, Zoom);

        _mouseVel = new Vector2(0, 0);

        _dlHandlerActive = false;
        _activeThreads = 0;
        _downloadList = new List<MapTile>();

        LoadMap((int) _targetTile.x, (int) _targetTile.y, Zoom);
    }

    private void LoadMap(int targX, int targY, int zoom)
    {
        var halfX = (int)Math.Floor(_noXTiles / 2f);
        var halfY = (int)Math.Floor(_noYTiles / 2f);

        var tileID = 0;

        for (var x = targX - halfX; x <= targX + halfX; x++)
        {
            for (var y = targY - halfY; y <= targY + halfY; y++)
            {
                var id = tileID;

                if (_tileCache.TileExists(x, y, zoom))
                    _mapTiles[id] = _tileCache.GetTile(x, y, zoom);
                else
                {
                    var tile = new MapTile(x, y, Zoom);
                    _mapTiles[id] = tile;

                    _tileCache.Add(tile);
                    _downloadList.Insert(0, tile);
                }

                tileID++;
            }
        }

        // delete fly-over tiles
        while (_downloadList.Count > _noXTiles*_noYTiles)
        {
            var lastTile = _downloadList[_downloadList.Count - 1];
            _tileCache.Remove(lastTile);
            _downloadList.Remove(lastTile);
        }

        if (_dlHandlerActive) return;
        new Thread(DownloadHandler).Start();
        _dlHandlerActive = true;
    }
    
    private void DownloadHandler()
    {
        while (_downloadList.Count > 0)
        {
            while (_activeThreads >= MaxThreads)
                Thread.Sleep(1);

            lock (_downloadList)
            {
                var firstTile = _downloadList[0];
                _downloadList.RemoveAt(0);

                new Thread(() => DownloadData(firstTile)).Start();
            }

            Interlocked.Increment(ref _activeThreads);
        }

        _dlHandlerActive = false;
    }

    private void DownloadData(MapTile tile)
    {
        var url = "http://141.28.104.22/osm/" + tile.Zoom + "/" + tile.X + "/" + tile.Y + ".png";

        using (var webClient = new WebClient())
        {
            test++;
            Debug.Log("Starting Download (" + _activeThreads + ", " + test + ")");
            //var data = 
            webClient.DownloadDataCompleted +=  delegate(object sender, DownloadDataCompletedEventArgs e)   
                      {   
                        tile.SetData(e.Result);  
                      };  
            webClient.DownloadDataAsync(new Uri(url));
            
            while (webClient.IsBusy) {Thread.Sleep(10);}
            Debug.Log("Download done (" + _activeThreads + ")");
            //tile.SetData(data);
        }
        Interlocked.Decrement(ref _activeThreads);
    }

    private void OnGUI()
    {
        var centerX = _noXTiles*256/2.0f - Screen.width/2.0f;
        var centerY = _noYTiles*256/2.0f - Screen.height/2.0f;

        var mapDiff = 256*new Vector2(0.5f - (_targetTile.x - (int) _targetTile.x),
            0.5f - (_targetTile.y - (int) _targetTile.y));

        for (var x = 0; x < _noXTiles; x++)
        {
            for (var y = 0; y < _noYTiles; y++)
            {
                var id = x*_noYTiles + y;
                var tile = _mapTiles[id];

                if (tile == null)
                    continue;

                if (tile.ToBeRendered)
                    tile.RenderData();

                GUI.DrawTexture(new Rect(x*256 - centerX + mapDiff.x,
                    y*256 - centerY + mapDiff.y, 256, 256), tile.Tex);
            }
        }
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
            _mousePos = Input.mousePosition;

        if (Input.GetMouseButton(0))
        {
            var mouseDiff = Input.mousePosition - _mousePos;

            _mouseVel.x = MovSpeed * mouseDiff.x;
            _mouseVel.y = MovSpeed * mouseDiff.y;

            _mousePos = Input.mousePosition;
        }
        else
        {
            var curDamp = (float)Math.Exp(-Damping * Time.deltaTime);

            _mouseVel.x *= curDamp;
            _mouseVel.y *= curDamp;
        }

        var newX = _targetTile.x - _mouseVel.x/256.0f;
        var newY = _targetTile.y + _mouseVel.y/256.0f;

        var diffX = (int) newX - (int) _targetTile.x;
        var diffY = (int) newY - (int) _targetTile.y;

        if (diffX != 0 || diffY != 0)
            LoadMap((int) newX, (int) newY, Zoom);

        _targetTile.x = newX;
        _targetTile.y = newY;
    }
}