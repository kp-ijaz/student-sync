import 'enums.dart';
import 'json.dart';

class SessionListItem {
  const SessionListItem({
    required this.id,
    required this.title,
    required this.sessionType,
    required this.status,
    required this.startUtc,
    required this.endUtc,
    required this.timeZoneId,
    required this.teacherId,
    required this.teacherName,
    this.teacherPhotoUrl,
    this.batchId,
    this.batchName,
    this.subjectName,
    required this.participantCount,
    required this.deliveryMode,
    required this.hasMeetingLink,
    required this.isRecurring,
  });

  final String id;
  final String title;
  final SessionType sessionType;
  final SessionStatus status;
  final DateTime startUtc;
  final DateTime endUtc;
  final String timeZoneId;
  final String teacherId;
  final String teacherName;
  final String? teacherPhotoUrl;
  final String? batchId;
  final String? batchName;
  final String? subjectName;
  final int participantCount;
  final DeliveryMode deliveryMode;
  final bool hasMeetingLink;
  final bool isRecurring;

  factory SessionListItem.fromJson(Json j) => SessionListItem(
        id: j.str('id'),
        title: j.str('title'),
        sessionType: j.enumValue('sessionType', SessionType.values),
        status: j.enumValue('status', SessionStatus.values),
        startUtc: j.date('startUtc'),
        endUtc: j.date('endUtc'),
        timeZoneId: j.str('timeZoneId'),
        teacherId: j.str('teacherId'),
        teacherName: j.str('teacherName'),
        teacherPhotoUrl: j.strOrNull('teacherPhotoUrl'),
        batchId: j.strOrNull('batchId'),
        batchName: j.strOrNull('batchName'),
        subjectName: j.strOrNull('subjectName'),
        participantCount: j.integer('participantCount'),
        deliveryMode: j.enumValue('deliveryMode', DeliveryMode.values),
        hasMeetingLink: j.boolean('hasMeetingLink'),
        isRecurring: j.boolean('isRecurring'),
      );
}

class Participant {
  const Participant({required this.studentId, required this.studentName, required this.status, this.attendance});
  final String studentId;
  final String studentName;
  final ParticipantStatus status;
  final AttendanceStatus? attendance;

  factory Participant.fromJson(Json j) => Participant(
        studentId: j.str('studentId'),
        studentName: j.str('studentName'),
        status: j.enumValue('status', ParticipantStatus.values),
        attendance: j.enumOrNull('attendance', AttendanceStatus.values),
      );
}

class SessionDetail {
  const SessionDetail({
    required this.id,
    required this.title,
    required this.sessionType,
    required this.status,
    required this.startUtc,
    required this.endUtc,
    required this.timeZoneId,
    required this.teacherId,
    required this.teacherName,
    this.teacherPhotoUrl,
    this.batchName,
    this.arrangementTitle,
    this.courseName,
    this.subjectName,
    this.chapterTitle,
    required this.deliveryMode,
    required this.meetingProvider,
    this.meetingUrl,
    this.location,
    this.objectives,
    this.cancellationReason,
    required this.canManage,
    required this.canMarkAttendance,
    required this.participantCount,
    required this.participants,
    required this.rowVersion,
  });

  final String id;
  final String title;
  final SessionType sessionType;
  final SessionStatus status;
  final DateTime startUtc;
  final DateTime endUtc;
  final String timeZoneId;
  final String teacherId;
  final String teacherName;
  final String? teacherPhotoUrl;
  final String? batchName;
  final String? arrangementTitle;
  final String? courseName;
  final String? subjectName;
  final String? chapterTitle;
  final DeliveryMode deliveryMode;
  final MeetingProvider meetingProvider;
  final String? meetingUrl;
  final String? location;
  final String? objectives;
  final String? cancellationReason;
  final bool canManage;
  final bool canMarkAttendance;
  final int participantCount;
  final List<Participant> participants;
  final String rowVersion;

  factory SessionDetail.fromJson(Json j) => SessionDetail(
        id: j.str('id'),
        title: j.str('title'),
        sessionType: j.enumValue('sessionType', SessionType.values),
        status: j.enumValue('status', SessionStatus.values),
        startUtc: j.date('startUtc'),
        endUtc: j.date('endUtc'),
        timeZoneId: j.str('timeZoneId'),
        teacherId: j.str('teacherId'),
        teacherName: j.str('teacherName'),
        teacherPhotoUrl: j.strOrNull('teacherPhotoUrl'),
        batchName: j.strOrNull('batchName'),
        arrangementTitle: j.strOrNull('arrangementTitle'),
        courseName: j.strOrNull('courseName'),
        subjectName: j.strOrNull('subjectName'),
        chapterTitle: j.strOrNull('chapterTitle'),
        deliveryMode: j.enumValue('deliveryMode', DeliveryMode.values),
        meetingProvider: j.enumValue('meetingProvider', MeetingProvider.values),
        meetingUrl: j.strOrNull('meetingUrl'),
        location: j.strOrNull('location'),
        objectives: j.strOrNull('objectives'),
        cancellationReason: j.strOrNull('cancellationReason'),
        canManage: j.boolean('canManage'),
        canMarkAttendance: j.boolean('canMarkAttendance'),
        participantCount: j.integer('participantCount'),
        participants: j.list('participants', Participant.fromJson),
        rowVersion: j.str('rowVersion'),
      );
}

class AttendanceRow {
  const AttendanceRow({required this.studentId, required this.studentName, this.status, this.note, this.markedAtUtc});
  final String studentId;
  final String studentName;
  final AttendanceStatus? status;
  final String? note;
  final DateTime? markedAtUtc;

  factory AttendanceRow.fromJson(Json j) => AttendanceRow(
        studentId: j.str('studentId'),
        studentName: j.str('studentName'),
        status: j.enumOrNull('status', AttendanceStatus.values),
        note: j.strOrNull('note'),
        markedAtUtc: j.dateOrNull('markedAtUtc'),
      );
}

class AttendanceRoster {
  const AttendanceRoster({required this.sessionId, required this.sessionStatus, required this.canEdit, required this.rows});
  final String sessionId;
  final SessionStatus sessionStatus;
  final bool canEdit;
  final List<AttendanceRow> rows;

  factory AttendanceRoster.fromJson(Json j) => AttendanceRoster(
        sessionId: j.str('sessionId'),
        sessionStatus: j.enumValue('sessionStatus', SessionStatus.values),
        canEdit: j.boolean('canEdit'),
        rows: j.list('rows', AttendanceRow.fromJson),
      );
}

class AttendanceEntry {
  const AttendanceEntry({required this.studentId, required this.status, this.note});
  final String studentId;
  final AttendanceStatus status;
  final String? note;
  Json toJson() => {'studentId': studentId, 'status': enumToJson(status), 'note': note};
}

class AgendaItem {
  const AgendaItem({required this.id, required this.kind, this.doubtId, required this.title, required this.isDiscussed, this.studentName});
  final String id;
  final AgendaItemKind kind;
  final String? doubtId;
  final String title;
  final bool isDiscussed;
  final String? studentName;

  factory AgendaItem.fromJson(Json j) => AgendaItem(
        id: j.str('id'),
        kind: j.enumValue('kind', AgendaItemKind.values),
        doubtId: j.strOrNull('doubtId'),
        title: j.str('title'),
        isDiscussed: j.boolean('isDiscussed'),
        studentName: j.strOrNull('studentName'),
      );
}

class PrepStudent {
  const PrepStudent({
    required this.studentId,
    required this.studentName,
    required this.missedRecentSessions,
    required this.pendingAssignments,
    required this.openDoubts,
  });
  final String studentId;
  final String studentName;
  final int missedRecentSessions;
  final int pendingAssignments;
  final int openDoubts;

  factory PrepStudent.fromJson(Json j) => PrepStudent(
        studentId: j.str('studentId'),
        studentName: j.str('studentName'),
        missedRecentSessions: j.integer('missedRecentSessions'),
        pendingAssignments: j.integer('pendingAssignments'),
        openDoubts: j.integer('openDoubts'),
      );
}
