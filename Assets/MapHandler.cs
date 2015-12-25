using System;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

public class MapTile
{
    public readonly int X;
    public readonly int Y;
    public readonly int Zoom;

    public readonly byte[] Data;

    public Texture2D Tex { private set; get; }

    internal MapTile()
    {
        X = Y = Zoom = -1;

        Tex = new Texture2D(1, 1, TextureFormat.RGB24, false);
        Tex.SetPixel(0, 0, new Color(252, 251, 231));
        Tex.Apply();
    }

    internal MapTile(int x, int y, int zoom, byte[] data)
    {
        X = x;
        Y = y;
        Zoom = zoom;
        Data = data;
    }

    internal void RenderTileTex()
    {
        Tex = new Texture2D(256, 256, TextureFormat.RGB24, false);
        Tex.LoadImage(Data);
    }

    public override bool Equals(object other)
    {
        var tile = other as MapTile;
        if (tile == null) return false;
        return (X == tile.X) && (Y == tile.Y) && (Zoom == tile.Zoom);
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
            hashCode = (hashCode * 397) ^ Y;
            hashCode = (hashCode * 397) ^ Zoom;
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
    private const int MaxThreads = 50;

    private const int Zoom = 16;
    private const float Lat = 47.9874f;
    private const float Long = 7.8945f;

    private volatile MapTile[] _mapTiles;
    private volatile List<MapTile> _tileCache; 
    private int _activeThreads;

    private int _noXTiles;
    private int _noYTiles;

    private Vector2 _targetTile;
    private Vector3 _mousePos;

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

        var dummyTile = new MapTile();

        _tileCache = new List<MapTile> {dummyTile};
        _mapTiles = Enumerable.Repeat(dummyTile, _noXTiles*_noYTiles).ToArray();

        var targetVec = new Vector2(Lat, Long);
        _targetTile = WorldToTilePos(targetVec.x, targetVec.y, Zoom);

        _activeThreads = 1;
        new Thread(() => LoadMap((int) _targetTile.x, (int) _targetTile.y, Zoom)).Start();
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
                    var cx = x;
                    var cy = y;

                    _mapTiles[id] = _tileCache.GetTile(-1, -1, -1);
                    new Thread(() => DownloadData(id, zoom, cx, cy)).Start();

                    Interlocked.Increment(ref _activeThreads);
                    while (_activeThreads > MaxThreads)
                        Thread.Sleep(1);
                }

                tileID++;
            }
        }

        Interlocked.Decrement(ref _activeThreads);
    }

    private void DownloadData(int id, int zoom, int x, int y)
    {
        var url = "http://141.28.104.22/osm/" + zoom + "/" + x + "/" + y + ".png";
        
        using (var webClient = new WebClient())
        {
            var data = webClient.DownloadData(url);
            var tile = new MapTile(x, y, Zoom, data);

            _mapTiles[id] = tile;
            _tileCache.Add(tile);
        }

        Interlocked.Decrement(ref _activeThreads);
    }

    private void OnGUI()
    {
        var centerX = _noXTiles*256/2.0f - Screen.width/2.0f;
        var centerY = _noYTiles*256/2.0f - Screen.height/2.0f;

        var mapDiff = 256 * new Vector2(0.5f - (_targetTile.x - (int) _targetTile.x),
            0.5f - (_targetTile.y - (int) _targetTile.y));

        for (var x = 0; x < _noXTiles; x++)
        {
            for (var y = 0; y < _noYTiles; y++)
            {
                var id = x*_noYTiles + y;

                var tile = _mapTiles[id];
                if (tile == null) continue;

                if (tile.Tex == null)
                    tile.RenderTileTex();

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
            var newX = _targetTile.x - mouseDiff.x/256.0f;
            var newY = _targetTile.y + mouseDiff.y/256.0f;

            var diffX = (int) newX - (int) _targetTile.x;
            var diffY = (int) newY - (int) _targetTile.y;

            if (diffX != 0 || diffY != 0)
                LoadMap((int) newX, (int) newY, Zoom);

             _targetTile.x = newX;
             _targetTile.y = newY;

            _mousePos = Input.mousePosition;
        }
    }
}