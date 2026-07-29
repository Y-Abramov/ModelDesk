using System;

namespace ModelDesk
{
    // Режим привязки станций source -> target при копировании конструкции.
    internal enum MappingMode { Absolute, Relative, Offset }

    // Чистый детерминированный пересчёт станции source -> target (без Robur).
    //   Absolute — 1:1 (s => s).
    //   Relative — линейно масштабирует диапазон src[min..max] в tgt[min..max];
    //              вырожденный src (min==max) -> tgtMin.
    //   Offset   — сдвиг начала src к началу tgt + пользовательский офсет.
    internal static class StationMapper
    {
        internal static Func<double, double> Build(
            MappingMode mode, double srcMin, double srcMax, double tgtMin, double tgtMax, double offset)
        {
            switch (mode)
            {
                case MappingMode.Relative:
                    double span = srcMax - srcMin;
                    return s => span > 1e-9
                        ? tgtMin + (s - srcMin) / span * (tgtMax - tgtMin)
                        : tgtMin;
                case MappingMode.Offset:
                    return s => s - srcMin + tgtMin + offset;
                default:
                    return s => s;
            }
        }
    }
}
