import 'json.dart';

class PagedResult<T> {
  const PagedResult({required this.items, required this.page, required this.pageSize, required this.totalCount});

  final List<T> items;
  final int page;
  final int pageSize;
  final int totalCount;

  bool get hasMore => page * pageSize < totalCount;

  factory PagedResult.fromJson(Json j, T Function(Json) parse) => PagedResult(
        items: j.list('items', parse),
        page: j.integer('page'),
        pageSize: j.integer('pageSize'),
        totalCount: j.integer('totalCount'),
      );
}

class LookupItem {
  const LookupItem({required this.id, required this.name, this.detail});

  final String id;
  final String name;
  final String? detail;

  factory LookupItem.fromJson(Json j) => LookupItem(id: j.str('id'), name: j.str('name'), detail: j.strOrNull('detail'));
}

class IdResponse {
  const IdResponse(this.id);
  final String id;
  factory IdResponse.fromJson(Json j) => IdResponse(j.str('id'));
}

class FileDto {
  const FileDto({required this.id, required this.fileName, required this.contentType, required this.sizeBytes});

  final String id;
  final String fileName;
  final String contentType;
  final int sizeBytes;

  factory FileDto.fromJson(Json j) => FileDto(
        id: j.str('id'),
        fileName: j.str('fileName'),
        contentType: j.str('contentType'),
        sizeBytes: j.integer('sizeBytes'),
      );
}

class FileLink {
  const FileLink({required this.url, required this.expiresAtUtc});
  final String url;
  final DateTime expiresAtUtc;
  factory FileLink.fromJson(Json j) => FileLink(url: j.str('url'), expiresAtUtc: j.date('expiresAtUtc'));
}

/// Evidence-based metric: value plus the plain-language explanation of how it is calculated.
class Metric {
  const Metric({required this.label, required this.value, required this.howCalculated, this.percent});

  final String label;
  final String value;
  final String howCalculated;
  final double? percent;

  factory Metric.fromJson(Json j) => Metric(
        label: j.str('label'),
        value: j.str('value'),
        howCalculated: j.str('howCalculated'),
        percent: j.numberOrNull('percent'),
      );
}

class Progress {
  const Progress({
    required this.studentId,
    required this.attendance,
    required this.assignmentsSubmitted,
    required this.averageAssignmentMark,
    required this.averageExamScore,
    required this.openDoubts,
    required this.lessonsAttended,
    required this.activeWeeks,
  });

  final String studentId;
  final Metric attendance;
  final Metric assignmentsSubmitted;
  final Metric averageAssignmentMark;
  final Metric averageExamScore;
  final Metric openDoubts;
  final Metric lessonsAttended;
  final Metric activeWeeks;

  List<Metric> get all =>
      [attendance, assignmentsSubmitted, averageAssignmentMark, averageExamScore, openDoubts, lessonsAttended, activeWeeks];

  factory Progress.fromJson(Json j) => Progress(
        studentId: j.str('studentId'),
        attendance: Metric.fromJson(j.obj('attendance')),
        assignmentsSubmitted: Metric.fromJson(j.obj('assignmentsSubmitted')),
        averageAssignmentMark: Metric.fromJson(j.obj('averageAssignmentMark')),
        averageExamScore: Metric.fromJson(j.obj('averageExamScore')),
        openDoubts: Metric.fromJson(j.obj('openDoubts')),
        lessonsAttended: Metric.fromJson(j.obj('lessonsAttended')),
        activeWeeks: Metric.fromJson(j.obj('activeWeeks')),
      );
}

class Stat {
  const Stat({required this.key, required this.label, required this.value, this.hint});
  final String key;
  final String label;
  final String value;
  final String? hint;
  factory Stat.fromJson(Json j) => Stat(key: j.str('key'), label: j.str('label'), value: j.str('value'), hint: j.strOrNull('hint'));
}
