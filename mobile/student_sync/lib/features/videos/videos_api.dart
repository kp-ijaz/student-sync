import '../../core/api/api_client.dart';
import '../../core/models/common.dart';
import '../../core/models/content_models.dart';
import '../../core/models/enums.dart';
import '../../core/models/json.dart';

/// Pre-recorded class videos. Uploading stays in the web app; the app lists, plays and
/// (for the class teacher) locks or unlocks them.
class VideosApi {
  VideosApi(this._api);
  final ApiClient _api;

  Future<List<PrerecordedVideo>> forSession(String sessionId) => _api.get(
        '/sessions/$sessionId/videos',
        (j) => (j! as List<dynamic>).cast<Json>().map(PrerecordedVideo.fromJson).toList(growable: false),
      );

  /// Videos across every class the user can see, newest class first.
  Future<PagedResult<PrerecordedVideo>> list({int page = 1, int pageSize = 50}) =>
      _api.get('/videos', (j) => PagedResult.fromJson(j! as Json, PrerecordedVideo.fromJson), query: {'page': page, 'pageSize': pageSize});

  /// Manual control: force unlocked, force locked, or back to the automatic rule.
  Future<void> setAccess(String videoId, VideoAccessOverride access) =>
      _api.post('/videos/$videoId/access', body: {'accessOverride': enumToJson(access)});
}
