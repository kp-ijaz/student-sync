import 'common.dart';
import 'content_models.dart';
import 'doubt_models.dart';
import 'enums.dart';
import 'json.dart';
import 'learning_models.dart';
import 'session_models.dart';

class StudentDashboard {
  const StudentDashboard({
    required this.firstName,
    this.nextSession,
    required this.upcomingSessions,
    required this.dueAssignments,
    required this.openDoubts,
    required this.recentContent,
    required this.progress,
    required this.notifications,
  });

  final String firstName;
  final SessionListItem? nextSession;
  final List<SessionListItem> upcomingSessions;
  final List<AssignmentListItem> dueAssignments;
  final List<DoubtListItem> openDoubts;
  final List<RecentContent> recentContent;
  final Progress progress;
  final List<AppNotification> notifications;

  factory StudentDashboard.fromJson(Json j) => StudentDashboard(
        firstName: j.str('firstName'),
        nextSession: j.objOrNull('nextSession') == null ? null : SessionListItem.fromJson(j.obj('nextSession')),
        upcomingSessions: j.list('upcomingSessions', SessionListItem.fromJson),
        dueAssignments: j.list('dueAssignments', AssignmentListItem.fromJson),
        openDoubts: j.list('openDoubts', DoubtListItem.fromJson),
        recentContent: j.list('recentContent', RecentContent.fromJson),
        progress: Progress.fromJson(j.obj('progress')),
        notifications: j.list('notifications', AppNotification.fromJson),
      );
}

class AiDraftSummaryWithSession {
  const AiDraftSummaryWithSession({required this.draftId, required this.sessionId, required this.sessionTitle, required this.createdAtUtc});
  final String draftId;
  final String sessionId;
  final String sessionTitle;
  final DateTime createdAtUtc;

  factory AiDraftSummaryWithSession.fromJson(Json j) => AiDraftSummaryWithSession(
        draftId: j.str('draftId'),
        sessionId: j.str('sessionId'),
        sessionTitle: j.str('sessionTitle'),
        createdAtUtc: j.date('createdAtUtc'),
      );
}

class TeacherDashboard {
  const TeacherDashboard({
    this.nextSession,
    required this.todaySessions,
    required this.upcomingSessions,
    required this.pendingAttendance,
    required this.unresolvedDoubts,
    required this.submissionsToGrade,
    required this.pendingAiApprovals,
    required this.stats,
  });

  final SessionListItem? nextSession;
  final List<SessionListItem> todaySessions;
  final List<SessionListItem> upcomingSessions;
  final List<SessionListItem> pendingAttendance;
  final List<DoubtListItem> unresolvedDoubts;
  final List<SubmissionToGrade> submissionsToGrade;
  final List<AiDraftSummaryWithSession> pendingAiApprovals;
  final List<Stat> stats;

  factory TeacherDashboard.fromJson(Json j) => TeacherDashboard(
        nextSession: j.objOrNull('nextSession') == null ? null : SessionListItem.fromJson(j.obj('nextSession')),
        todaySessions: j.list('todaySessions', SessionListItem.fromJson),
        upcomingSessions: j.list('upcomingSessions', SessionListItem.fromJson),
        pendingAttendance: j.list('pendingAttendance', SessionListItem.fromJson),
        unresolvedDoubts: j.list('unresolvedDoubts', DoubtListItem.fromJson),
        submissionsToGrade: j.list('submissionsToGrade', SubmissionToGrade.fromJson),
        pendingAiApprovals: j.list('pendingAiApprovals', AiDraftSummaryWithSession.fromJson),
        stats: j.list('stats', Stat.fromJson),
      );
}

class AttendanceItem {
  const AttendanceItem({required this.sessionId, required this.sessionTitle, required this.startUtc, required this.sessionType, required this.status});
  final String sessionId;
  final String sessionTitle;
  final DateTime startUtc;
  final SessionType sessionType;
  final AttendanceStatus status;

  factory AttendanceItem.fromJson(Json j) => AttendanceItem(
        sessionId: j.str('sessionId'),
        sessionTitle: j.str('sessionTitle'),
        startUtc: j.date('startUtc'),
        sessionType: j.enumValue('sessionType', SessionType.values),
        status: j.enumValue('status', AttendanceStatus.values),
      );
}

class ChildOverview {
  const ChildOverview({
    required this.studentId,
    required this.name,
    required this.upcomingSessions,
    required this.recentAttendance,
    required this.assignments,
    required this.results,
    required this.progress,
  });

  final String studentId;
  final String name;
  final List<SessionListItem> upcomingSessions;
  final List<AttendanceItem> recentAttendance;
  final List<AssignmentListItem> assignments;
  final List<ExamResultItem> results;
  final Progress progress;

  factory ChildOverview.fromJson(Json j) => ChildOverview(
        studentId: j.str('studentId'),
        name: j.str('name'),
        upcomingSessions: j.list('upcomingSessions', SessionListItem.fromJson),
        recentAttendance: j.list('recentAttendance', AttendanceItem.fromJson),
        assignments: j.list('assignments', AssignmentListItem.fromJson),
        results: j.list('results', ExamResultItem.fromJson),
        progress: Progress.fromJson(j.obj('progress')),
      );
}

class ParentDashboard {
  const ParentDashboard({required this.children, this.selectedChildId, this.child});
  final List<LookupItem> children;
  final String? selectedChildId;
  final ChildOverview? child;

  factory ParentDashboard.fromJson(Json j) => ParentDashboard(
        children: j.list('children', LookupItem.fromJson),
        selectedChildId: j.strOrNull('selectedChildId'),
        child: j.objOrNull('child') == null ? null : ChildOverview.fromJson(j.obj('child')),
      );
}

class ClassPreparation {
  const ClassPreparation({required this.session, required this.agenda, required this.openDoubts, required this.students});
  final SessionDetail session;
  final List<AgendaItem> agenda;
  final List<DoubtListItem> openDoubts;
  final List<PrepStudent> students;

  factory ClassPreparation.fromJson(Json j) => ClassPreparation(
        session: SessionDetail.fromJson(j.obj('session')),
        agenda: j.list('agenda', AgendaItem.fromJson),
        openDoubts: j.list('openDoubts', DoubtListItem.fromJson),
        students: j.list('students', PrepStudent.fromJson),
      );
}

class StudentSummary {
  const StudentSummary({
    required this.id,
    required this.fullName,
    required this.email,
    required this.studentCode,
    this.gradeLevelName,
    this.schoolName,
    required this.status,
    required this.timeZoneId,
    this.photoUrl,
  });

  final String id;
  final String fullName;
  final String email;
  final String studentCode;
  final String? gradeLevelName;
  final String? schoolName;
  final PersonStatus status;
  final String timeZoneId;
  final String? photoUrl;

  factory StudentSummary.fromJson(Json j) => StudentSummary(
        id: j.str('id'),
        fullName: j.str('fullName'),
        email: j.str('email'),
        studentCode: j.str('studentCode'),
        gradeLevelName: j.strOrNull('gradeLevelName'),
        schoolName: j.strOrNull('schoolName'),
        status: j.enumValue('status', PersonStatus.values),
        timeZoneId: j.str('timeZoneId'),
        photoUrl: j.strOrNull('photoUrl'),
      );
}

class StudentProfile {
  const StudentProfile({required this.student, required this.courses, required this.batches, required this.arrangements, required this.parents, required this.progress});
  final StudentSummary student;
  final List<LookupItem> courses;
  final List<LookupItem> batches;
  final List<LookupItem> arrangements;
  final List<LookupItem> parents;
  final Progress progress;

  factory StudentProfile.fromJson(Json j) => StudentProfile(
        student: StudentSummary.fromJson(j.obj('student')),
        courses: j.list('courses', LookupItem.fromJson),
        batches: j.list('batches', LookupItem.fromJson),
        arrangements: j.list('arrangements', LookupItem.fromJson),
        parents: j.list('parents', LookupItem.fromJson),
        progress: Progress.fromJson(j.obj('progress')),
      );
}
