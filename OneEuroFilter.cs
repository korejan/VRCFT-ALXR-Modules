using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using LibALXR;

namespace ALXR
{
    using Vector3OneEuroFilter = OneEuroFilter<Vector3, Vector3FilterTrats, Vector3LowPassFilter, Vector3LowPassFilter>;
    using Vector2OneEuroFilter = OneEuroFilter<Vector2, Vector2FilterTrats, Vector2LowPassFilter, Vector2LowPassFilter>;
    using QuaternionOneEuroFilter = OneEuroFilter<Quaternion, QuaternionFilterTrats, QuaternionLowPassFilter, QuaternionLowPassFilter>;

    public sealed class XrPosef1EuroFilterParams
    {
        [JsonInclude]
        public OneEuroFilterParams RotParams { get; set; } = OneEuroFilterParams.Default;

        [JsonInclude]
        public OneEuroFilterParams PosParams { get; set; } = OneEuroFilterParams.Default;
    }

    public sealed class XrPosefOneEuroFilter
    {
        private QuaternionOneEuroFilter rotFilter = new QuaternionOneEuroFilter();
        private Vector3OneEuroFilter    posFilter = new Vector3OneEuroFilter();

        public XrPosef1EuroFilterParams FilterParams
        {
            get => new XrPosef1EuroFilterParams()
            {
                RotParams = this.RotParams,
                PosParams = this.PosParams
            };
            set
            {
                RotParams = value.RotParams;
                PosParams = value.PosParams;
            }
        }

        public OneEuroFilterParams RotParams {
            get => rotFilter.Params;
            set => rotFilter.Params = value;
        }

        public OneEuroFilterParams PosParams
        {
            get => posFilter.Params;
            set => posFilter.Params = value;
        }

        public void Reset()
        {
            rotFilter.Reset();
            posFilter.Reset();
        }

        public ALXRPosef Filter(float dt, ALXRPosef x)
        {
            return new ALXRPosef()
            {
                orientation = rotFilter.Filter(dt, x.orientation),
                position = posFilter.Filter(dt, x.position)
            };
        }
    }

    public interface ILowPassFilter<T> where T : struct
    {
        T Filter(T x, float alpha);
        T HatxPrev { get; }

        void Reset();
    }

    public sealed class Vector2LowPassFilter : ILowPassFilter<Vector2>
    {
        public Vector2 HatxPrev { get; private set; } = Vector2.Zero;

        private bool firstTime = true;

        public void Reset() => firstTime = true;

        public Vector2 Filter(Vector2 x, float alpha)
        {
            if (firstTime)
            {
                firstTime = false;
                HatxPrev = x;
            }
            var hatx = alpha * x + (1.0f - alpha) * HatxPrev;
            return HatxPrev = hatx;
        }
    }

    public sealed class Vector3LowPassFilter : ILowPassFilter<Vector3>
    {
        public Vector3 HatxPrev { get; private set; } = Vector3.Zero;

        private bool firstTime = true;

        public void Reset() => firstTime = true;

        public Vector3 Filter(Vector3 x, float alpha)
        {
            if (firstTime)
            {
                firstTime = false;
                HatxPrev = x;
            }
            var hatx = alpha * x + (1.0f - alpha) * HatxPrev;
            return HatxPrev = hatx;
        }
    }

    public sealed class QuaternionLowPassFilter : ILowPassFilter<Quaternion>
    {
        public Quaternion HatxPrev { get; private set; } = Quaternion.Identity;

        private bool firstTime = true;

        public void Reset() => firstTime = true;

        public Quaternion Filter(Quaternion x, float alpha)
        {
            if (firstTime)
            {
                firstTime = false;
                HatxPrev = x;
            }
            return HatxPrev = Quaternion.Slerp(HatxPrev, x, alpha);
        }
    }

    public interface IFilterTraits<T> where T : struct
    {
        T Identity { get; }
        T ComputeDerivative(T prev, T current, float dt);
        float ComputeDerivativeMagnitude(T dx);
    }

    struct Vector2FilterTrats : IFilterTraits<Vector2>
    {
        public Vector2 Identity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Vector2.Zero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2 ComputeDerivative(Vector2 prev, Vector2 current, float dt)
        {
            return (current - prev) * (1.0f / dt);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ComputeDerivativeMagnitude(Vector2 dx)
        {
            return dx.Length();
        }
    }

    struct Vector3FilterTrats : IFilterTraits<Vector3>
    {
        public Vector3 Identity {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Vector3.Zero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 ComputeDerivative(Vector3 prev, Vector3 current, float dt)
        {
            return (current - prev) * (1.0f / dt);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ComputeDerivativeMagnitude(Vector3 dx)
        {
            return dx.Length();
        }

    }

    struct QuaternionFilterTrats : IFilterTraits<Quaternion>
    {
        public Quaternion Identity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Quaternion.Identity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion ComputeDerivative(Quaternion prev, Quaternion current, float dt)
        {
            float rate = 1.0f / dt;
            var inversePrev = Quaternion.Inverse(prev);
            var dx = current * inversePrev;
            dx.X *= rate;
            dx.Y *= rate;
            dx.Z *= rate;
            dx.W = dx.W * rate + (1.0f - rate);
            return Quaternion.Normalize(dx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ComputeDerivativeMagnitude(Quaternion dx)
        {
            return (float)(2.0 * Math.Acos(dx.W));
        }
    }

    public struct OneEuroFilterParams
    {
        [JsonInclude]
        public float MinCutoff;
        [JsonInclude]
        public float Beta;
        [JsonInclude]
        public float DCutoff;

        public static OneEuroFilterParams Default
        {
            get => new OneEuroFilterParams()
            {
                MinCutoff = 1.0f,
                Beta = 0.5f,
                DCutoff = 1.0f
            };
        }
    }

    public sealed class OneEuroFilter<T, TTraits, XFilterT, DxFilterT>
        where T : struct
        where TTraits : IFilterTraits<T>, new()
        where XFilterT : ILowPassFilter<T>, new()
        where DxFilterT : ILowPassFilter<T>, new()        
    {
        public OneEuroFilterParams Params { get; set; } = OneEuroFilterParams.Default;

        private XFilterT xFilter = new XFilterT();
        private DxFilterT dxFilter = new DxFilterT();

        private readonly TTraits Traits = new TTraits();

        private bool firstTime = true;

        public void Reset() {
            dxFilter.Reset();
            xFilter.Reset();
            firstTime = true;
        }

        public T Filter(float dt, T x)
        {
            T dx;
            if (firstTime)
            {
                firstTime = false;
                dx = Traits.Identity;
            }
            else
            {
                dx = Traits.ComputeDerivative(xFilter.HatxPrev, x, dt);
            }

            float derivativeMag = Traits.ComputeDerivativeMagnitude(dxFilter.Filter(dx, Alpha(dt, Params.DCutoff)));
            float cutoff = Params.MinCutoff + Params.Beta * derivativeMag;

            return xFilter.Filter(x, Alpha(dt, cutoff));
        }

        private static float Alpha(float dt, float cutoff)
        {
            float tau = (float)(1.0 / (2.0 * Math.PI * cutoff));
            return 1.0f / (1.0f + tau / dt);
        }
    }
}