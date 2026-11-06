// Mirrors the domain enums in src/StudentSync.Domain. The server serialises them as
// PascalCase strings; see parseEnum/enumToJson in json.dart.

enum UserRole { superAdmin, institutionAdmin, teacher, student, parent }

enum SessionType { batch, group, oneToOne, privateGroup }

enum SessionStatus { scheduled, live, completed, cancelled }

enum DeliveryMode { online, inPerson, hybrid }

enum MeetingProvider { none, manual, googleMeet }

enum ParticipantStatus { active, removed }

enum AttendanceStatus { present, late, absent, excused }

enum DoubtStatus { newDoubt, assigned, inProgress, answered, pendingConfirmation, resolved, reopened }

enum DoubtPriority { low, normal, high, urgent }

enum QuestionType { singleChoice, multipleChoice, trueFalse, shortAnswer, longAnswer, fillBlank, matching, numerical }

enum ExamStatus { draft, published, closed }

enum AttemptStatus { inProgress, submitted, graded }

enum AssignmentStatus { draft, published, closed }

enum SubmissionStatus { draft, submitted, graded, returnedForCorrection }

enum NoteVisibility { teacherOnly, participants }

enum PublicationStatus { draft, published }

enum ContentSource { manual, aiApproved }

enum MaterialKind { file, link }

enum RecordingStatus { pending, available, failed, deleted }

enum VideoUnlockRule { beforeClass, afterClass }

enum VideoAccessOverride { automatic, unlocked, locked }

enum PersonStatus { active, inactive, archived, suspended }

enum AgendaItemKind { doubt, topic, note }

/// The server value `New` cannot be a Dart enum member (`new` is a keyword), so the
/// Dart member is `newDoubt` and is mapped explicitly here.
DoubtStatus parseDoubtStatus(String raw) {
  if (raw == 'New') return DoubtStatus.newDoubt;
  for (final v in DoubtStatus.values) {
    if (v.name.toLowerCase() == raw.toLowerCase()) return v;
  }
  throw FormatException('Unknown DoubtStatus value "$raw"');
}
