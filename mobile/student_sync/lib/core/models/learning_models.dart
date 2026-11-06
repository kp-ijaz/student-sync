import 'common.dart';
import 'enums.dart';
import 'json.dart';

// ---------- Assignments ----------

class AssignmentListItem {
  const AssignmentListItem({
    required this.id,
    required this.title,
    required this.status,
    required this.dueAtUtc,
    required this.maxMarks,
    required this.teacherName,
    this.sessionTitle,
    required this.recipientCount,
    required this.submittedCount,
    required this.gradedCount,
    this.mySubmissionStatus,
    this.myMarks,
  });

  final String id;
  final String title;
  final AssignmentStatus status;
  final DateTime dueAtUtc;
  final double maxMarks;
  final String teacherName;
  final String? sessionTitle;
  final int recipientCount;
  final int submittedCount;
  final int gradedCount;
  final SubmissionStatus? mySubmissionStatus;
  final double? myMarks;

  factory AssignmentListItem.fromJson(Json j) => AssignmentListItem(
        id: j.str('id'),
        title: j.str('title'),
        status: j.enumValue('status', AssignmentStatus.values),
        dueAtUtc: j.date('dueAtUtc'),
        maxMarks: j.number('maxMarks'),
        teacherName: j.str('teacherName'),
        sessionTitle: j.strOrNull('sessionTitle'),
        recipientCount: j.integer('recipientCount'),
        submittedCount: j.integer('submittedCount'),
        gradedCount: j.integer('gradedCount'),
        mySubmissionStatus: j.enumOrNull('mySubmissionStatus', SubmissionStatus.values),
        myMarks: j.numberOrNull('myMarks'),
      );
}

class AssignmentDetail {
  const AssignmentDetail({
    required this.id,
    required this.title,
    required this.instructionsMarkdown,
    required this.status,
    required this.dueAtUtc,
    required this.maxMarks,
    required this.allowLateSubmission,
    required this.allowResubmission,
    required this.teacherName,
    this.sessionId,
    this.sessionTitle,
    required this.attachments,
    required this.canManage,
    required this.rowVersion,
  });

  final String id;
  final String title;
  final String instructionsMarkdown;
  final AssignmentStatus status;
  final DateTime dueAtUtc;
  final double maxMarks;
  final bool allowLateSubmission;
  final bool allowResubmission;
  final String teacherName;
  final String? sessionId;
  final String? sessionTitle;
  final List<FileDto> attachments;
  final bool canManage;
  final String rowVersion;

  factory AssignmentDetail.fromJson(Json j) => AssignmentDetail(
        id: j.str('id'),
        title: j.str('title'),
        instructionsMarkdown: j.str('instructionsMarkdown'),
        status: j.enumValue('status', AssignmentStatus.values),
        dueAtUtc: j.date('dueAtUtc'),
        maxMarks: j.number('maxMarks'),
        allowLateSubmission: j.boolean('allowLateSubmission'),
        allowResubmission: j.boolean('allowResubmission'),
        teacherName: j.str('teacherName'),
        sessionId: j.strOrNull('sessionId'),
        sessionTitle: j.strOrNull('sessionTitle'),
        attachments: j.list('attachments', FileDto.fromJson),
        canManage: j.boolean('canManage'),
        rowVersion: j.str('rowVersion'),
      );
}

class Submission {
  const Submission({
    required this.id,
    required this.assignmentId,
    required this.assignmentTitle,
    required this.studentId,
    required this.studentName,
    required this.status,
    this.textAnswer,
    this.submittedAtUtc,
    required this.isLate,
    required this.attemptNumber,
    this.marks,
    required this.maxMarks,
    this.feedback,
    this.gradedAtUtc,
    required this.files,
    required this.rowVersion,
  });

  final String id;
  final String assignmentId;
  final String assignmentTitle;
  final String studentId;
  final String studentName;
  final SubmissionStatus status;
  final String? textAnswer;
  final DateTime? submittedAtUtc;
  final bool isLate;
  final int attemptNumber;
  final double? marks;
  final double maxMarks;
  final String? feedback;
  final DateTime? gradedAtUtc;
  final List<FileDto> files;
  final String rowVersion;

  factory Submission.fromJson(Json j) => Submission(
        id: j.str('id'),
        assignmentId: j.str('assignmentId'),
        assignmentTitle: j.str('assignmentTitle'),
        studentId: j.str('studentId'),
        studentName: j.str('studentName'),
        status: j.enumValue('status', SubmissionStatus.values),
        textAnswer: j.strOrNull('textAnswer'),
        submittedAtUtc: j.dateOrNull('submittedAtUtc'),
        isLate: j.boolean('isLate'),
        attemptNumber: j.integer('attemptNumber'),
        marks: j.numberOrNull('marks'),
        maxMarks: j.number('maxMarks'),
        feedback: j.strOrNull('feedback'),
        gradedAtUtc: j.dateOrNull('gradedAtUtc'),
        files: j.list('files', FileDto.fromJson),
        rowVersion: j.str('rowVersion'),
      );
}

class SubmissionToGrade {
  const SubmissionToGrade({
    required this.submissionId,
    required this.assignmentId,
    required this.assignmentTitle,
    required this.studentName,
    this.submittedAtUtc,
    required this.isLate,
  });
  final String submissionId;
  final String assignmentId;
  final String assignmentTitle;
  final String studentName;
  final DateTime? submittedAtUtc;
  final bool isLate;

  factory SubmissionToGrade.fromJson(Json j) => SubmissionToGrade(
        submissionId: j.str('submissionId'),
        assignmentId: j.str('assignmentId'),
        assignmentTitle: j.str('assignmentTitle'),
        studentName: j.str('studentName'),
        submittedAtUtc: j.dateOrNull('submittedAtUtc'),
        isLate: j.boolean('isLate'),
      );
}

// ---------- Exams ----------

class ExamListItem {
  const ExamListItem({
    required this.id,
    required this.title,
    required this.status,
    required this.opensAtUtc,
    required this.closesAtUtc,
    required this.durationMinutes,
    required this.questionCount,
    required this.totalMarks,
    required this.subjectName,
    required this.resultsReleased,
    required this.attemptsUsed,
    required this.maxAttempts,
    this.myBestScore,
    required this.hasInProgressAttempt,
    this.latestAttemptId,
  });

  final String id;
  final String title;
  final ExamStatus status;
  final DateTime opensAtUtc;
  final DateTime closesAtUtc;
  final int durationMinutes;
  final int questionCount;
  final double totalMarks;
  final String subjectName;
  final bool resultsReleased;
  final int attemptsUsed;
  final int maxAttempts;
  final double? myBestScore;
  final bool hasInProgressAttempt;

  /// The student's most recent attempt, used to reopen its result screen.
  final String? latestAttemptId;

  /// "View result" is offered for a finished attempt only, never one still in progress.
  bool get canViewResult => !hasInProgressAttempt && latestAttemptId != null;

  bool isOpenAt(DateTime nowUtc) => status == ExamStatus.published && !nowUtc.isBefore(opensAtUtc) && nowUtc.isBefore(closesAtUtc);
  bool get hasAttemptsLeft => attemptsUsed < maxAttempts;

  factory ExamListItem.fromJson(Json j) => ExamListItem(
        id: j.str('id'),
        title: j.str('title'),
        status: j.enumValue('status', ExamStatus.values),
        opensAtUtc: j.date('opensAtUtc'),
        closesAtUtc: j.date('closesAtUtc'),
        durationMinutes: j.integer('durationMinutes'),
        questionCount: j.integer('questionCount'),
        totalMarks: j.number('totalMarks'),
        subjectName: j.str('subjectName'),
        resultsReleased: j.boolean('resultsReleased'),
        attemptsUsed: j.integer('attemptsUsed'),
        maxAttempts: j.integer('maxAttempts'),
        myBestScore: j.numberOrNull('myBestScore'),
        hasInProgressAttempt: j.boolean('hasInProgressAttempt'),
        latestAttemptId: j.strOrNull('latestAttemptId'),
      );
}

/// A student's answer. Only the fields relevant to the question type are used
/// (mirrors `ExamResponse` in the domain).
class ExamResponse {
  const ExamResponse({this.optionIds, this.text, this.number, this.matches});

  final List<String>? optionIds;
  final String? text;
  final num? number;

  /// Left-hand option id → chosen match text.
  final Map<String, String>? matches;

  bool get isEmpty =>
      (optionIds == null || optionIds!.isEmpty) &&
      (text == null || text!.trim().isEmpty) &&
      number == null &&
      (matches == null || matches!.isEmpty);

  factory ExamResponse.fromJson(Json j) => ExamResponse(
        optionIds: (j['optionIds'] as List<dynamic>?)?.cast<String>(),
        text: j.strOrNull('text'),
        number: j['number'] as num?,
        matches: (j['matches'] as Map<String, dynamic>?)?.map((k, v) => MapEntry(k, v as String)),
      );

  Json toJson() => {'optionIds': optionIds, 'text': text, 'number': number, 'matches': matches};
}

class AttemptOption {
  const AttemptOption({required this.id, required this.text});
  final String id;
  final String text;
  factory AttemptOption.fromJson(Json j) => AttemptOption(id: j.str('id'), text: j.str('text'));
}

class AttemptQuestion {
  const AttemptQuestion({
    required this.examQuestionId,
    required this.type,
    required this.stem,
    required this.marks,
    required this.options,
    this.matchChoices,
    this.savedResponse,
  });

  final String examQuestionId;
  final QuestionType type;
  final String stem;
  final double marks;
  final List<AttemptOption> options;
  final List<String>? matchChoices;
  final ExamResponse? savedResponse;

  factory AttemptQuestion.fromJson(Json j) => AttemptQuestion(
        examQuestionId: j.str('examQuestionId'),
        type: j.enumValue('type', QuestionType.values),
        stem: j.str('stem'),
        marks: j.number('marks'),
        options: j.list('options', AttemptOption.fromJson),
        matchChoices: (j['matchChoices'] as List<dynamic>?)?.cast<String>(),
        savedResponse: j.objOrNull('savedResponse') == null ? null : ExamResponse.fromJson(j.obj('savedResponse')),
      );
}

class Attempt {
  const Attempt({
    required this.attemptId,
    required this.examId,
    required this.examTitle,
    required this.status,
    required this.startedAtUtc,
    required this.deadlineUtc,
    required this.attemptNumber,
    required this.questions,
  });

  final String attemptId;
  final String examId;
  final String examTitle;
  final AttemptStatus status;
  final DateTime startedAtUtc;
  final DateTime deadlineUtc;
  final int attemptNumber;
  final List<AttemptQuestion> questions;

  factory Attempt.fromJson(Json j) => Attempt(
        attemptId: j.str('attemptId'),
        examId: j.str('examId'),
        examTitle: j.str('examTitle'),
        status: j.enumValue('status', AttemptStatus.values),
        startedAtUtc: j.date('startedAtUtc'),
        deadlineUtc: j.date('deadlineUtc'),
        attemptNumber: j.integer('attemptNumber'),
        questions: j.list('questions', AttemptQuestion.fromJson),
      );
}

class AnswerResult {
  const AnswerResult({
    required this.examQuestionId,
    required this.stem,
    required this.type,
    this.response,
    this.isCorrect,
    this.awardedMarks,
    required this.marks,
    this.feedback,
    this.explanation,
    this.correctAnswer,
  });

  final String examQuestionId;
  final String stem;
  final QuestionType type;
  final ExamResponse? response;
  final bool? isCorrect;
  final double? awardedMarks;
  final double marks;
  final String? feedback;
  final String? explanation;
  final String? correctAnswer;

  factory AnswerResult.fromJson(Json j) => AnswerResult(
        examQuestionId: j.str('examQuestionId'),
        stem: j.str('stem'),
        type: j.enumValue('type', QuestionType.values),
        response: j.objOrNull('response') == null ? null : ExamResponse.fromJson(j.obj('response')),
        isCorrect: j.boolOrNull('isCorrect'),
        awardedMarks: j.numberOrNull('awardedMarks'),
        marks: j.number('marks'),
        feedback: j.strOrNull('feedback'),
        explanation: j.strOrNull('explanation'),
        correctAnswer: j.strOrNull('correctAnswer'),
      );
}

class AttemptResult {
  const AttemptResult({
    required this.attemptId,
    required this.examTitle,
    required this.studentName,
    required this.status,
    this.submittedAtUtc,
    this.score,
    required this.totalMarks,
    required this.requiresManualGrading,
    required this.resultsReleased,
    required this.answers,
  });

  final String attemptId;
  final String examTitle;
  final String studentName;
  final AttemptStatus status;
  final DateTime? submittedAtUtc;
  final double? score;
  final double totalMarks;
  final bool requiresManualGrading;
  final bool resultsReleased;
  final List<AnswerResult> answers;

  factory AttemptResult.fromJson(Json j) => AttemptResult(
        attemptId: j.str('attemptId'),
        examTitle: j.str('examTitle'),
        studentName: j.str('studentName'),
        status: j.enumValue('status', AttemptStatus.values),
        submittedAtUtc: j.dateOrNull('submittedAtUtc'),
        score: j.numberOrNull('score'),
        totalMarks: j.number('totalMarks'),
        requiresManualGrading: j.boolean('requiresManualGrading'),
        resultsReleased: j.boolean('resultsReleased'),
        answers: j.list('answers', AnswerResult.fromJson),
      );
}

class ExamResultItem {
  const ExamResultItem({required this.examId, required this.examTitle, this.submittedAtUtc, this.score, required this.totalMarks});
  final String examId;
  final String examTitle;
  final DateTime? submittedAtUtc;
  final double? score;
  final double totalMarks;

  factory ExamResultItem.fromJson(Json j) => ExamResultItem(
        examId: j.str('examId'),
        examTitle: j.str('examTitle'),
        submittedAtUtc: j.dateOrNull('submittedAtUtc'),
        score: j.numberOrNull('score'),
        totalMarks: j.number('totalMarks'),
      );
}
