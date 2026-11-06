import '../../core/api/api_client.dart';
import '../../core/models/common.dart';
import '../../core/models/doubt_models.dart';
import '../../core/models/json.dart';

class DoubtsApi {
  DoubtsApi(this._api);
  final ApiClient _api;

  Future<PagedResult<DoubtListItem>> list({bool unresolvedOnly = false, String? sessionId, int page = 1, int pageSize = 50}) => _api.get(
        '/doubts',
        (j) => PagedResult.fromJson(j! as Json, DoubtListItem.fromJson),
        query: {'unresolvedOnly': unresolvedOnly ? true : null, 'sessionId': sessionId, 'page': page, 'pageSize': pageSize},
      );

  Future<DoubtDetail> detail(String id) => _api.get('/doubts/$id', (j) => DoubtDetail.fromJson(j! as Json));

  Future<String> create({
    required String title,
    required String description,
    String? subjectId,
    String? chapterId,
    String? sessionId,
    required bool isPrivate,
    required String idempotencyKey,
  }) async {
    final json = await _api.post('/doubts', body: {
      'title': title,
      'description': description,
      'subjectId': subjectId,
      'chapterId': chapterId,
      'sessionId': sessionId,
      'recordingId': null,
      'recordingTimestampSeconds': null,
      'isPrivate': isPrivate,
      'fileIds': null,
    }, headers: {'Idempotency-Key': idempotencyKey});
    return IdResponse.fromJson(json! as Json).id;
  }

  Future<void> reply(String id, {required String body, bool markAnswered = false}) =>
      _api.post('/doubts/$id/replies', body: {'body': body, 'fileIds': null, 'markAnswered': markAnswered});

  Future<void> confirm(String id, {required bool resolved, String? comment}) =>
      _api.post('/doubts/$id/confirm', body: {'resolved': resolved, 'comment': (comment == null || comment.trim().isEmpty) ? null : comment.trim()});

  Future<List<Subject>> subjects() =>
      _api.get('/academics/subjects', (j) => (j! as List<dynamic>).cast<Json>().map(Subject.fromJson).toList(growable: false));
}
