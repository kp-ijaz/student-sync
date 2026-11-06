import '../../core/api/api_client.dart';
import '../../core/models/json.dart';
import '../../core/models/subject_models.dart';

/// Subjects the signed-in user has classes in, and each subject's syllabus.
class SubjectsApi {
  SubjectsApi(this._api);
  final ApiClient _api;

  Future<List<MySubject>> mine() =>
      _api.get('/my/subjects', (j) => (j! as List<dynamic>).cast<Json>().map(MySubject.fromJson).toList(growable: false));

  Future<SubjectSyllabus> syllabus(String subjectId) => _api.get('/my/subjects/$subjectId', (j) => SubjectSyllabus.fromJson(j! as Json));
}
