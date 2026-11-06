/// Small, strict JSON readers used by every model. Payloads mirror the C# records
/// in `src/StudentSync.Contracts` (camelCase; enums as strings; UTC ISO-8601 dates).
/// A missing or mistyped required field throws [FormatException] so contract drift
/// surfaces as a visible error instead of silently wrong data.
library;

typedef Json = Map<String, dynamic>;

extension JsonRead on Json {
  T _req<T>(String key) {
    final v = this[key];
    if (v is T) return v;
    throw FormatException('Expected "$key" to be $T but was ${v.runtimeType}');
  }

  String str(String key) => _req<String>(key);
  String? strOrNull(String key) => this[key] as String?;

  bool boolean(String key) => _req<bool>(key);
  bool? boolOrNull(String key) => this[key] as bool?;

  int integer(String key) => _req<num>(key).toInt();
  int? intOrNull(String key) => (this[key] as num?)?.toInt();

  double number(String key) => _req<num>(key).toDouble();
  double? numberOrNull(String key) => (this[key] as num?)?.toDouble();

  DateTime date(String key) => parseUtc(str(key));
  DateTime? dateOrNull(String key) {
    final s = strOrNull(key);
    return s == null ? null : parseUtc(s);
  }

  Json obj(String key) => (_req<Map<String, dynamic>>(key));
  Json? objOrNull(String key) => this[key] as Map<String, dynamic>?;

  List<T> list<T>(String key, T Function(Json) parse) =>
      ((this[key] as List<dynamic>?) ?? const []).cast<Map<String, dynamic>>().map(parse).toList(growable: false);

  List<String> strings(String key) => ((this[key] as List<dynamic>?) ?? const []).cast<String>().toList(growable: false);

  T enumValue<T extends Enum>(String key, List<T> values) => parseEnum(values, str(key));
  T? enumOrNull<T extends Enum>(String key, List<T> values) {
    final s = strOrNull(key);
    return s == null ? null : parseEnum(values, s);
  }
}

/// Parses a server timestamp as UTC. The API emits `Z`-suffixed values; a value
/// without an offset is treated as UTC (never as device-local time).
DateTime parseUtc(String raw) {
  final hasZone = raw.endsWith('Z') || RegExp(r'[+-]\d{2}:?\d{2}$').hasMatch(raw.substring(10));
  return DateTime.parse(hasZone ? raw : '${raw}Z').toUtc();
}

String toUtcIso(DateTime value) => value.toUtc().toIso8601String();

/// Enum names on the server are PascalCase (`OneToOne`); Dart enums are camelCase (`oneToOne`).
T parseEnum<T extends Enum>(List<T> values, String raw) {
  final lower = raw.toLowerCase();
  for (final v in values) {
    if (v.name.toLowerCase() == lower) return v;
  }
  throw FormatException('Unknown ${T.toString()} value "$raw"');
}

String enumToJson(Enum value) => value.name[0].toUpperCase() + value.name.substring(1);
