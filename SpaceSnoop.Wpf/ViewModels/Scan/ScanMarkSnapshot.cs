namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal readonly record struct ScanMarkSnapshot(SpaceBase Root, SpaceBase Scope, int Count, long Bytes);
