import 'package:flutter/material.dart';

import '../models/enums.dart';
import 'tokens.dart';

/// One mapping from every status enum to a label + colour, shared by every screen.
/// Mirrors DESIGN.md §5 and `StatusStyles.cs` on the web.
@immutable
class StatusStyle {
  const StatusStyle(this.label, this.color, {this.strikeThrough = false});

  final String label;

  /// Semantic colour. Badges draw a tinted background with darkened text so the
  /// label meets contrast requirements; colour is never the only signal.
  final Color color;

  /// Cancelled sessions show their title struck through.
  final bool strikeThrough;

  @override
  bool operator ==(Object other) => other is StatusStyle && other.label == label && other.color == color && other.strikeThrough == strikeThrough;

  @override
  int get hashCode => Object.hash(label, color, strikeThrough);
}

abstract final class StatusStyles {
  static StatusStyle session(SessionStatus s) => switch (s) {
        SessionStatus.scheduled => const StatusStyle('Scheduled', SsColors.blue),
        SessionStatus.live => const StatusStyle('Live', SsColors.orange),
        SessionStatus.completed => const StatusStyle('Completed', SsColors.success),
        SessionStatus.cancelled => const StatusStyle('Cancelled', SsColors.slate, strikeThrough: true),
      };

  static StatusStyle attendance(AttendanceStatus? s) => switch (s) {
        AttendanceStatus.present => const StatusStyle('Present', SsColors.success),
        AttendanceStatus.late => const StatusStyle('Late', SsColors.warning),
        AttendanceStatus.absent => const StatusStyle('Absent', SsColors.error),
        AttendanceStatus.excused => const StatusStyle('Excused', SsColors.slate),
        null => const StatusStyle('Not marked', SsColors.slate),
      };

  static StatusStyle doubt(DoubtStatus s) => switch (s) {
        DoubtStatus.newDoubt => const StatusStyle('New', SsColors.blue),
        DoubtStatus.assigned => const StatusStyle('Assigned', SsColors.blue),
        DoubtStatus.inProgress => const StatusStyle('In progress', SsColors.info),
        DoubtStatus.answered => const StatusStyle('Answered', SsColors.warning),
        DoubtStatus.pendingConfirmation => const StatusStyle('Awaiting confirmation', SsColors.warning),
        DoubtStatus.resolved => const StatusStyle('Resolved', SsColors.success),
        DoubtStatus.reopened => const StatusStyle('Reopened', SsColors.error),
      };

  static StatusStyle submission(SubmissionStatus? s) => switch (s) {
        null => const StatusStyle('Not started', SsColors.slate),
        SubmissionStatus.draft => const StatusStyle('Draft saved', SsColors.info),
        SubmissionStatus.submitted => const StatusStyle('Submitted', SsColors.blue),
        SubmissionStatus.graded => const StatusStyle('Graded', SsColors.success),
        SubmissionStatus.returnedForCorrection => const StatusStyle('Returned for correction', SsColors.warning),
      };

  static StatusStyle assignment(AssignmentStatus s) => switch (s) {
        AssignmentStatus.draft => const StatusStyle('Draft', SsColors.slate),
        AssignmentStatus.published => const StatusStyle('Open', SsColors.blue),
        AssignmentStatus.closed => const StatusStyle('Closed', SsColors.slate),
      };

  static StatusStyle exam(ExamStatus s) => switch (s) {
        ExamStatus.draft => const StatusStyle('Draft', SsColors.slate),
        ExamStatus.published => const StatusStyle('Published', SsColors.blue),
        ExamStatus.closed => const StatusStyle('Closed', SsColors.slate),
      };

  static StatusStyle attempt(AttemptStatus s) => switch (s) {
        AttemptStatus.inProgress => const StatusStyle('In progress', SsColors.orange),
        AttemptStatus.submitted => const StatusStyle('Submitted', SsColors.blue),
        AttemptStatus.graded => const StatusStyle('Graded', SsColors.success),
      };

  static StatusStyle publication(PublicationStatus s) => switch (s) {
        PublicationStatus.draft => const StatusStyle('Draft', SsColors.warning),
        PublicationStatus.published => const StatusStyle('Published', SsColors.success),
      };

  static StatusStyle recording(RecordingStatus s) => switch (s) {
        RecordingStatus.pending => const StatusStyle('Processing', SsColors.warning),
        RecordingStatus.available => const StatusStyle('Available', SsColors.success),
        RecordingStatus.failed => const StatusStyle('Failed', SsColors.error),
        RecordingStatus.deleted => const StatusStyle('Deleted', SsColors.slate),
      };

  /// Pre-recorded video lock state, matching the web badges.
  static StatusStyle videoLock({required bool isUnlocked, required VideoAccessOverride access}) => switch ((isUnlocked, access)) {
        (true, VideoAccessOverride.unlocked) => const StatusStyle('Unlocked by teacher', SsColors.success),
        (true, _) => const StatusStyle('Unlocked', SsColors.success),
        (false, VideoAccessOverride.locked) => const StatusStyle('Locked by teacher', SsColors.error),
        (false, _) => const StatusStyle('Locked', SsColors.slate),
      };

  static StatusStyle priority(DoubtPriority p) => switch (p) {
        DoubtPriority.low => const StatusStyle('Low priority', SsColors.slate),
        DoubtPriority.normal => const StatusStyle('Normal priority', SsColors.slate),
        DoubtPriority.high => const StatusStyle('High priority', SsColors.warning),
        DoubtPriority.urgent => const StatusStyle('Urgent', SsColors.error),
      };

  /// Teaching-mode labels (DESIGN.md §5).
  static String sessionTypeLabel(SessionType t) => switch (t) {
        SessionType.batch => 'Batch class',
        SessionType.group => 'Group session',
        SessionType.oneToOne => 'One-to-one',
        SessionType.privateGroup => 'Private group',
      };

  static String deliveryModeLabel(DeliveryMode m) => switch (m) {
        DeliveryMode.online => 'Online',
        DeliveryMode.inPerson => 'In person',
        DeliveryMode.hybrid => 'Hybrid',
      };

  static String questionTypeLabel(QuestionType t) => switch (t) {
        QuestionType.singleChoice => 'Choose one answer',
        QuestionType.multipleChoice => 'Choose all that apply',
        QuestionType.trueFalse => 'True or false',
        QuestionType.shortAnswer => 'Short answer',
        QuestionType.longAnswer => 'Long answer',
        QuestionType.fillBlank => 'Fill in the blank',
        QuestionType.matching => 'Match each item',
        QuestionType.numerical => 'Numerical answer',
      };

  static String roleLabel(UserRole r) => switch (r) {
        UserRole.superAdmin => 'Platform administrator',
        UserRole.institutionAdmin => 'Institution administrator',
        UserRole.teacher => 'Teacher',
        UserRole.student => 'Student',
        UserRole.parent => 'Parent',
      };
}

/// Darkens a semantic colour for text on its own 12% tint so it reaches ≥4.5:1.
Color badgeTextColor(Color base) {
  final hsl = HSLColor.fromColor(base);
  return hsl.withLightness((hsl.lightness * 0.55).clamp(0.0, 1.0)).toColor();
}
