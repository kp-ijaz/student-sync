import 'common.dart';
import 'enums.dart';
import 'json.dart';

class DoubtListItem {
  const DoubtListItem({
    required this.id,
    required this.title,
    required this.status,
    required this.priority,
    required this.studentName,
    this.subjectName,
    this.assignedTeacherName,
    this.sessionId,
    this.followUpSessionId,
    this.followUpSessionStartUtc,
    required this.createdAtUtc,
    required this.lastActivityAtUtc,
    required this.replyCount,
  });

  final String id;
  final String title;
  final DoubtStatus status;
  final DoubtPriority priority;
  final String studentName;
  final String? subjectName;
  final String? assignedTeacherName;
  final String? sessionId;
  final String? followUpSessionId;
  final DateTime? followUpSessionStartUtc;
  final DateTime createdAtUtc;
  final DateTime lastActivityAtUtc;
  final int replyCount;

  factory DoubtListItem.fromJson(Json j) => DoubtListItem(
        id: j.str('id'),
        title: j.str('title'),
        status: parseDoubtStatus(j.str('status')),
        priority: j.enumValue('priority', DoubtPriority.values),
        studentName: j.str('studentName'),
        subjectName: j.strOrNull('subjectName'),
        assignedTeacherName: j.strOrNull('assignedTeacherName'),
        sessionId: j.strOrNull('sessionId'),
        followUpSessionId: j.strOrNull('followUpSessionId'),
        followUpSessionStartUtc: j.dateOrNull('followUpSessionStartUtc'),
        createdAtUtc: j.date('createdAtUtc'),
        lastActivityAtUtc: j.date('lastActivityAtUtc'),
        replyCount: j.integer('replyCount'),
      );
}

class DoubtMessage {
  const DoubtMessage({
    required this.id,
    required this.authorName,
    required this.isTeacherReply,
    required this.body,
    required this.createdAtUtc,
    required this.attachments,
  });
  final String id;
  final String authorName;
  final bool isTeacherReply;
  final String body;
  final DateTime createdAtUtc;
  final List<FileDto> attachments;

  factory DoubtMessage.fromJson(Json j) => DoubtMessage(
        id: j.str('id'),
        authorName: j.str('authorName'),
        isTeacherReply: j.boolean('isTeacherReply'),
        body: j.str('body'),
        createdAtUtc: j.date('createdAtUtc'),
        attachments: j.list('attachments', FileDto.fromJson),
      );
}

class DoubtHistory {
  const DoubtHistory({required this.atUtc, required this.from, required this.to, required this.changedBy, this.note});
  final DateTime atUtc;
  final DoubtStatus from;
  final DoubtStatus to;
  final String changedBy;
  final String? note;

  factory DoubtHistory.fromJson(Json j) => DoubtHistory(
        atUtc: j.date('atUtc'),
        from: parseDoubtStatus(j.str('from')),
        to: parseDoubtStatus(j.str('to')),
        changedBy: j.str('changedBy'),
        note: j.strOrNull('note'),
      );
}

class DoubtDetail {
  const DoubtDetail({
    required this.id,
    required this.title,
    required this.description,
    required this.status,
    required this.priority,
    required this.isPrivate,
    required this.studentId,
    required this.studentName,
    this.subjectName,
    this.sessionId,
    this.sessionTitle,
    this.assignedTeacherName,
    this.followUpSessionId,
    this.followUpSessionTitle,
    this.followUpSessionStartUtc,
    required this.messages,
    required this.history,
    required this.attachments,
    required this.canAnswer,
    required this.canConfirm,
    required this.rowVersion,
  });

  final String id;
  final String title;
  final String description;
  final DoubtStatus status;
  final DoubtPriority priority;
  final bool isPrivate;
  final String studentId;
  final String studentName;
  final String? subjectName;
  final String? sessionId;
  final String? sessionTitle;
  final String? assignedTeacherName;
  final String? followUpSessionId;
  final String? followUpSessionTitle;
  final DateTime? followUpSessionStartUtc;
  final List<DoubtMessage> messages;
  final List<DoubtHistory> history;
  final List<FileDto> attachments;
  final bool canAnswer;
  final bool canConfirm;
  final String rowVersion;

  factory DoubtDetail.fromJson(Json j) => DoubtDetail(
        id: j.str('id'),
        title: j.str('title'),
        description: j.str('description'),
        status: parseDoubtStatus(j.str('status')),
        priority: j.enumValue('priority', DoubtPriority.values),
        isPrivate: j.boolean('isPrivate'),
        studentId: j.str('studentId'),
        studentName: j.str('studentName'),
        subjectName: j.strOrNull('subjectName'),
        sessionId: j.strOrNull('sessionId'),
        sessionTitle: j.strOrNull('sessionTitle'),
        assignedTeacherName: j.strOrNull('assignedTeacherName'),
        followUpSessionId: j.strOrNull('followUpSessionId'),
        followUpSessionTitle: j.strOrNull('followUpSessionTitle'),
        followUpSessionStartUtc: j.dateOrNull('followUpSessionStartUtc'),
        messages: j.list('messages', DoubtMessage.fromJson),
        history: j.list('history', DoubtHistory.fromJson),
        attachments: j.list('attachments', FileDto.fromJson),
        canAnswer: j.boolean('canAnswer'),
        canConfirm: j.boolean('canConfirm'),
        rowVersion: j.str('rowVersion'),
      );
}

class Subject {
  const Subject({required this.id, required this.courseName, required this.code, required this.name, required this.chapters});
  final String id;
  final String courseName;
  final String code;
  final String name;
  final List<LookupItem> chapters;

  factory Subject.fromJson(Json j) => Subject(
        id: j.str('id'),
        courseName: j.str('courseName'),
        code: j.str('code'),
        name: j.str('name'),
        chapters: j.list('chapters', (c) => LookupItem(id: c.str('id'), name: c.str('title'))),
      );
}
