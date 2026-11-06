import '../../core/api/api_client.dart';
import '../../core/models/auth_models.dart';
import '../../core/models/common.dart';
import '../../core/models/content_models.dart';
import '../../core/models/json.dart';

class ProfileApi {
  ProfileApi(this._api);
  final ApiClient _api;

  Future<CurrentUser> me() => _api.get('/me', (j) => CurrentUser.fromJson(j! as Json));

  Future<CurrentUser> updateProfile({required String fullName, required String timeZoneId, required String preferredLanguage}) async {
    final json = await _api.put('/me/profile', body: {'fullName': fullName, 'timeZoneId': timeZoneId, 'preferredLanguage': preferredLanguage});
    return CurrentUser.fromJson(json! as Json);
  }

  /// Sets the signed-in user's profile photo (JPEG or PNG, at most 5 MB).
  Future<CurrentUser> uploadPhoto({required List<int> bytes, required String filename, required String contentType}) async {
    final json = await _api.sendFile('PUT', '/me/photo', bytes: bytes, filename: filename, contentType: contentType);
    return CurrentUser.fromJson(json! as Json);
  }

  Future<CurrentUser> removePhoto() async => CurrentUser.fromJson((await _api.delete('/me/photo'))! as Json);

  Future<PagedResult<AppNotification>> notifications({bool unreadOnly = false, int page = 1, int pageSize = 30}) => _api.get(
        '/notifications',
        (j) => PagedResult.fromJson(j! as Json, AppNotification.fromJson),
        query: {'unreadOnly': unreadOnly ? true : null, 'page': page, 'pageSize': pageSize},
      );

  Future<UnreadCounts> unread() => _api.get('/notifications/unread', (j) => UnreadCounts.fromJson(j! as Json));

  Future<void> markRead(String id) => _api.post('/notifications/$id/read');

  Future<void> markAllRead() => _api.post('/notifications/read');
}
