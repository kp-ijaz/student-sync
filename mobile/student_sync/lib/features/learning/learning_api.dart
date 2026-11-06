import '../../core/api/api_client.dart';
import '../../core/models/common.dart';
import '../../core/models/json.dart';
import '../../core/models/learning_models.dart';

class LearningApi {
  LearningApi(this._api);
  final ApiClient _api;

  /// Assignments the user can see; only those of one class when [sessionId] is given.
  Future<PagedResult<AssignmentListItem>> assignments({String? sessionId, int page = 1, int pageSize = 50}) => _api.get(
        '/assignments',
        (j) => PagedResult.fromJson(j! as Json, AssignmentListItem.fromJson),
        query: {'sessionId': sessionId, 'page': page, 'pageSize': pageSize},
      );

  Future<AssignmentDetail> assignment(String id) => _api.get('/assignments/$id', (j) => AssignmentDetail.fromJson(j! as Json));

  /// The student's own submission, or null when they have not started one (204).
  Future<Submission?> mySubmission(String assignmentId) =>
      _api.get('/assignments/$assignmentId/my-submission', (j) => j == null ? null : Submission.fromJson(j as Json));

  Future<void> saveSubmission(String assignmentId, {required String textAnswer, required bool submit, String? rowVersion, required String idempotencyKey}) =>
      _api.put(
        '/assignments/$assignmentId/my-submission',
        body: {'textAnswer': textAnswer, 'fileIds': null, 'submit': submit, 'rowVersion': rowVersion},
        headers: {'Idempotency-Key': idempotencyKey},
      );

  Future<List<Submission>> submissions(String assignmentId) => _api.get(
        '/assignments/$assignmentId/submissions',
        (j) => (j! as List<dynamic>).cast<Json>().map(Submission.fromJson).toList(growable: false),
      );

  Future<void> grade(String submissionId, {required double marks, String? feedback, required bool returnForCorrection, required String rowVersion}) =>
      _api.post('/submissions/$submissionId/grade', body: {
        'marks': marks,
        'feedback': (feedback == null || feedback.trim().isEmpty) ? null : feedback.trim(),
        'returnForCorrection': returnForCorrection,
        'rowVersion': rowVersion,
      });

  /// Teacher: creates an assignment for a class's students; published straight away when [publish].
  Future<String> createAssignment({
    required String sessionId,
    required String title,
    required String instructionsMarkdown,
    required DateTime dueAtUtc,
    required double maxMarks,
    required bool allowLateSubmission,
    required bool publish,
  }) async {
    final res = await _api.post('/assignments', body: {
      'title': title,
      'instructionsMarkdown': instructionsMarkdown,
      'dueAtUtc': toUtcIso(dueAtUtc),
      'maxMarks': maxMarks,
      'allowLateSubmission': allowLateSubmission,
      'allowResubmission': false,
      'sessionId': sessionId,
      'batchId': null,
      'subjectId': null,
      'studentIds': null,
      'rowVersion': null,
    });
    final id = (res! as Json).str('id');
    if (publish) await _api.post('/assignments/$id/publish');
    return id;
  }

  Future<List<ExamListItem>> exams() =>
      _api.get('/exams', (j) => (j! as List<dynamic>).cast<Json>().map(ExamListItem.fromJson).toList(growable: false));

  /// Starts a new attempt, or resumes the in-progress one (the server is idempotent).
  Future<Attempt> startAttempt(String examId) async => Attempt.fromJson((await _api.post('/exams/$examId/attempts'))! as Json);

  Future<void> saveAnswer(String attemptId, String examQuestionId, ExamResponse response) =>
      _api.put('/attempts/$attemptId/answers', body: {'examQuestionId': examQuestionId, 'response': response.toJson()});

  Future<AttemptResult> submitAttempt(String attemptId) async => AttemptResult.fromJson((await _api.post('/attempts/$attemptId/submit'))! as Json);

  Future<AttemptResult> result(String attemptId) => _api.get('/attempts/$attemptId/result', (j) => AttemptResult.fromJson(j! as Json));
}
