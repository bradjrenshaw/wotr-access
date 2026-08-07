using System;
using UnityEngine;

namespace WrathAccess.Audio
{
    /// <summary>Four directional wall-tone voices (order: ahead, behind, right, left — ear-fixed pans;
    /// the WallToneSystem rotates the TRACE directions by the listener facing), driven every frame with
    /// the trace hit point + a 0..1 proximity volume per direction. Dispose stops + releases the voices.
    /// (The Wwise backend and its engine-choice interface are gone — NAudio is the one audio engine.)</summary>
    internal interface IWallTones : IDisposable
    {
        void Update(Vector3[] hits, float[] volumes);
    }
}
