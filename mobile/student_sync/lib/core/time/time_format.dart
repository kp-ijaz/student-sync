import 'package:intl/intl.dart';
import 'package:timezone/timezone.dart' as tz;

/// Formats UTC instants in the viewer's IANA time zone (never the device's offset),
/// and adds the organiser's zone when it differs (DESIGN.md §6).
class TimeFormatter {
  TimeFormatter(this.viewerZoneId) : _viewer = _location(viewerZoneId);

  final String viewerZoneId;
  final tz.Location _viewer;

  /// Falls back to UTC for an unknown id rather than guessing a device offset.
  static tz.Location _location(String id) {
    try {
      return tz.getLocation(id);
    } on Exception {
      return tz.UTC;
    } on Error {
      return tz.UTC;
    }
  }

  static bool isKnownZone(String id) {
    try {
      tz.getLocation(id);
      return true;
    } on Exception {
      return false;
    } on Error {
      return false;
    }
  }

  tz.TZDateTime inViewerZone(DateTime utc) => tz.TZDateTime.from(utc.toUtc(), _viewer);
  tz.TZDateTime inZone(DateTime utc, String zoneId) => tz.TZDateTime.from(utc.toUtc(), _location(zoneId));

  bool sameZone(String zoneId) => _location(zoneId).name == _viewer.name;

  /// "Sat 7 Nov · 10:00"
  String dateTime(DateTime utc) => DateFormat('EEE d MMM · HH:mm').format(inViewerZone(utc));

  /// "Sat 7 Nov 2026"
  String date(DateTime utc) => DateFormat('EEE d MMM y').format(inViewerZone(utc));

  /// "10:00"
  String time(DateTime utc) => DateFormat('HH:mm').format(inViewerZone(utc));

  /// "Sat 7 Nov · 10:00–11:00" in the viewer's zone. Spans across midnight show both dates.
  String range(DateTime startUtc, DateTime endUtc) => _range(inViewerZone(startUtc), inViewerZone(endUtc));

  /// The same range in the organiser's zone, e.g. "06:00–07:00 Asia/Dubai", or null
  /// when the organiser uses the viewer's zone.
  String? organiserRange(DateTime startUtc, DateTime endUtc, String organiserZoneId) {
    if (sameZone(organiserZoneId)) return null;
    final s = inZone(startUtc, organiserZoneId);
    final e = inZone(endUtc, organiserZoneId);
    final sameDayAsViewer = DateFormat('yMd').format(s) == DateFormat('yMd').format(inViewerZone(startUtc));
    final prefix = sameDayAsViewer ? '' : '${DateFormat('EEE d MMM').format(s)} · ';
    return '$prefix${DateFormat('HH:mm').format(s)}–${DateFormat('HH:mm').format(e)} $organiserZoneId';
  }

  String _range(tz.TZDateTime s, tz.TZDateTime e) {
    final sameDay = s.year == e.year && s.month == e.month && s.day == e.day;
    final start = DateFormat('EEE d MMM · HH:mm').format(s);
    final end = sameDay ? DateFormat('HH:mm').format(e) : DateFormat('EEE d MMM · HH:mm').format(e);
    return '$start–$end';
  }

  /// The viewer's calendar date for [utc], as a date-only value (`DateTime.utc(y, m, d)`).
  DateTime viewerDate(DateTime utc) {
    final d = inViewerZone(utc);
    return DateTime.utc(d.year, d.month, d.day);
  }

  /// The UTC instant of local midnight at the start of [date] in the viewer's zone
  /// (DST-aware; only the date part of [date] is used).
  DateTime startOfViewerDayUtc(DateTime date) => tz.TZDateTime(_viewer, date.year, date.month, date.day).toUtc();

  /// Calendar-day key in the viewer's zone, for grouping ("2026-11-07").
  String dayKey(DateTime utc) => DateFormat('y-MM-dd').format(inViewerZone(utc));

  /// "Today", "Tomorrow" or "Sat 7 Nov" relative to [nowUtc] in the viewer's zone.
  String dayLabel(DateTime utc, DateTime nowUtc) {
    final d = inViewerZone(utc);
    final n = inViewerZone(nowUtc);
    final day = DateTime.utc(d.year, d.month, d.day);
    final today = DateTime.utc(n.year, n.month, n.day);
    final diff = day.difference(today).inDays;
    if (diff == 0) return 'Today';
    if (diff == 1) return 'Tomorrow';
    if (diff == -1) return 'Yesterday';
    return DateFormat('EEE d MMM').format(d);
  }

  /// "+04:00" style offset of [zoneId] at [atUtc].
  static String offsetLabel(String zoneId, DateTime atUtc) {
    final loc = _location(zoneId);
    final offset = tz.TZDateTime.from(atUtc, loc).timeZoneOffset;
    final sign = offset.isNegative ? '-' : '+';
    final abs = offset.abs();
    final h = abs.inHours.toString().padLeft(2, '0');
    final m = (abs.inMinutes % 60).toString().padLeft(2, '0');
    return 'UTC$sign$h:$m';
  }
}

/// "2 h 5 min", "45 min", "0:42" style helpers.
String formatDuration(Duration d) {
  if (d.isNegative) return '0 min';
  final h = d.inHours;
  final m = d.inMinutes % 60;
  if (h > 0) return m == 0 ? '$h h' : '$h h $m min';
  return '${d.inMinutes} min';
}

String formatCountdown(Duration d) {
  if (d.isNegative) d = Duration.zero;
  final h = d.inHours;
  final m = (d.inMinutes % 60).toString().padLeft(2, '0');
  final s = (d.inSeconds % 60).toString().padLeft(2, '0');
  return h > 0 ? '$h:$m:$s' : '$m:$s';
}

/// 8.0 → "8", 7.5 → "7.5"
String formatMarks(num value) {
  if (value == value.roundToDouble()) return value.toInt().toString();
  return value.toStringAsFixed(2).replaceFirst(RegExp(r'0+$'), '').replaceFirst(RegExp(r'\.$'), '');
}
