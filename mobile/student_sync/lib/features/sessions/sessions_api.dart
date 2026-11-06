import '../../core/api/api_client.dart';
import '../../core/models/common.dart';
import '../../core/models/content_models.dart';
import '../../core/models/dashboard_models.dart';
import '../../core/models/enums.dart';
import '../../core/models/json.dart';
import '../../core/models/session_models.dart';

List<T> _list<T>(Object? json, T Function(Json) parse) => (json! as List<dynamic>).cast<Json>().map(parse).toList(growable: false);

class SessionsApi {
  SessionsApi(this._api);
  final ApiClient _api;

  /// Sessions ending at or after [fromUtc] (ascending), or — with only [toUtc] —
  /// sessions starting before it (most recent first).
  Future<List<SessionListItem>> list({DateTime? fromUtc, DateTime? toUtc, int limit = 100}) =>
      _api.get('/sessions', (j) => _list(j, SessionListItem.fromJson), query: {'fromUtc': fromUtc, 'toUtc': toUtc, 'limit': limit});

  Future<SessionDetail> detail(String id) => _api.get('/sessions/$id', (j) => SessionDetail.fromJson(j! as Json));

  Future<List<Note>> notes(String id) => _api.get('/sessions/$id/notes', (j) => _list(j, Note.fromJson));

  /// Teacher: adds a note to the class; published straight away when [publish].
  Future<String> addNote(String sessionId, {required String title, required String bodyMarkdown, required bool publish}) async {
    final res = await _api.post('/sessions/$sessionId/notes', body: {
      'title': title,
      'bodyMarkdown': bodyMarkdown,
      'visibility': enumToJson(NoteVisibility.participants),
      'availableOffline': false,
      'rowVersion': null,
    });
    final id = (res! as Json).str('id');
    if (publish) await _api.post('/notes/$id/publish');
    return id;
  }

  /// Teacher: uploads a pre-recorded video for the class from a file on the device.
  /// It unlocks for students [offsetMinutes] before or after the class ([rule]).
  Future<String> uploadVideo(
    String sessionId, {
    required String filePath,
    required String filename,
    required String contentType,
    required String title,
    String? description,
    required VideoUnlockRule rule,
    required int offsetMinutes,
  }) async {
    final res = await _api.sendMultipart(
      'POST',
      '/sessions/$sessionId/videos',
      file: () => ApiClient.filePart(filePath, filename: filename, contentType: contentType),
      fields: {
        'title': title,
        if (description != null && description.isNotEmpty) 'description': description,
        'unlockRule': enumToJson(rule),
        'unlockOffsetMinutes': '$offsetMinutes',
      },
      timeout: const Duration(minutes: 15),
    );
    return (res! as Json).str('id');
  }

  Future<List<LearningMaterial>> materials(String id) =>
      _api.get('/sessions/$id/materials', (j) => PagedResult.fromJson(j! as Json, LearningMaterial.fromJson).items);

  Future<List<Recording>> recordings(String id) => _api.get('/sessions/$id/recordings', (j) => _list(j, Recording.fromJson));

  /// Students only (the server rejects other roles).
  Future<void> saveRecordingProgress(String recordingId, {required int positionSeconds, required bool completed}) =>
      _api.post('/recordings/$recordingId/progress', body: {'positionSeconds': positionSeconds, 'completed': completed});

  Future<AttendanceRoster> attendance(String id) => _api.get('/sessions/$id/attendance', (j) => AttendanceRoster.fromJson(j! as Json));

  Future<void> markAttendance(String id, List<AttendanceEntry> entries) =>
      _api.put('/sessions/$id/attendance', body: {'entries': entries.map((e) => e.toJson()).toList()});

  Future<ClassPreparation> preparation(String id) => _api.get('/sessions/$id/preparation', (j) => ClassPreparation.fromJson(j! as Json));

  /// Short-lived, user-bound download link for a private file (materials, attachments).
  Future<FileLink> fileLink(String fileId) => _api.get('/files/$fileId/link', (j) => FileLink.fromJson(j! as Json));
}
