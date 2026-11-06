import '../../core/api/api_client.dart';
import '../../core/models/common.dart';
import '../../core/models/dashboard_models.dart';
import '../../core/models/json.dart';

class DashboardApi {
  DashboardApi(this._api);
  final ApiClient _api;

  Future<StudentDashboard> student() => _api.get('/dashboard/student', (j) => StudentDashboard.fromJson(j! as Json));

  Future<TeacherDashboard> teacher() => _api.get('/dashboard/teacher', (j) => TeacherDashboard.fromJson(j! as Json));

  Future<ParentDashboard> parent({String? childId}) =>
      _api.get('/dashboard/parent', (j) => ParentDashboard.fromJson(j! as Json), query: {'childId': childId});

  Future<List<LookupItem>> myChildren() =>
      _api.get('/me/children', (j) => (j! as List<dynamic>).cast<Json>().map(LookupItem.fromJson).toList(growable: false));

  Future<PagedResult<StudentSummary>> students({String? search, int page = 1, int pageSize = 50}) => _api.get(
        '/students',
        (j) => PagedResult.fromJson(j! as Json, StudentSummary.fromJson),
        query: {'search': (search == null || search.trim().isEmpty) ? null : search.trim(), 'page': page, 'pageSize': pageSize},
      );

  Future<StudentProfile> studentProfile(String id) => _api.get('/students/$id', (j) => StudentProfile.fromJson(j! as Json));
}
