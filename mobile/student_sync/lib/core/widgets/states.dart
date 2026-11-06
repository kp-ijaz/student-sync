import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../api/api_exception.dart';
import '../theme/tokens.dart';
import 'brand.dart';
import 'illustration.dart';

/// Full-area loading state: the spinning brand mark with a short label.
class LoadingView extends StatelessWidget {
  const LoadingView({super.key, this.label = 'Loading'});
  final String label;

  @override
  Widget build(BuildContext context) => Center(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          BrandSpinner(size: 56, label: label),
          const SizedBox(height: SsSpace.s4),
          ExcludeSemantics(child: Text('$label…', style: Theme.of(context).textTheme.bodySmall)),
        ]),
      );
}

/// Loading state for one section of a longer page.
class SectionSkeleton extends StatelessWidget {
  const SectionSkeleton({super.key});

  @override
  Widget build(BuildContext context) => const Padding(
        padding: EdgeInsets.symmetric(vertical: SsSpace.s5),
        child: Center(child: BrandSpinner(size: 32)),
      );
}

class _MessageView extends StatelessWidget {
  const _MessageView({required this.icon, required this.title, this.message, this.action, this.iconColor = SsColors.blue, this.illustration});
  final IconData icon;
  final String title;
  final String? message;
  final Widget? action;
  final Color iconColor;

  /// Shown instead of the icon when given (and when there is room for it).
  final Illustration? illustration;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    return LayoutBuilder(builder: (context, c) {
      final roomy = !c.hasBoundedHeight || c.maxHeight >= 420;
      return Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(SsSpace.s5),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 360),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                if (illustration != null && roomy)
                  IllustrationView(illustration!, height: 170)
                else
                  // Icon on two soft concentric discs.
                  ExcludeSemantics(
                    child: Container(
                      width: 104,
                      height: 104,
                      decoration: BoxDecoration(shape: BoxShape.circle, color: iconColor.withValues(alpha: 0.06)),
                      alignment: Alignment.center,
                      child: Container(
                        width: 72,
                        height: 72,
                        decoration: BoxDecoration(
                          shape: BoxShape.circle,
                          color: SsColors.white,
                          boxShadow: SsShadow.card,
                          border: Border.all(color: iconColor.withValues(alpha: 0.12)),
                        ),
                        child: Icon(icon, size: 30, color: iconColor),
                      ),
                    ),
                  ),
                const SizedBox(height: SsSpace.s5),
                Text(title, style: t.titleLarge?.copyWith(fontSize: 19), textAlign: TextAlign.center),
                if (message != null) ...[
                  const SizedBox(height: SsSpace.s2),
                  Text(message!, style: t.bodyMedium?.copyWith(color: SsColors.textSecondary), textAlign: TextAlign.center),
                ],
                if (action != null) ...[const SizedBox(height: SsSpace.s5), action!],
              ],
            ),
          ),
        ),
      );
    });
  }
}

class EmptyState extends StatelessWidget {
  const EmptyState({super.key, required this.title, this.message, this.icon = LucideIcons.inbox, this.action, this.illustration = Illustration.empty});
  final String title;
  final String? message;
  final IconData icon;
  final Widget? action;
  final Illustration? illustration;

  @override
  Widget build(BuildContext context) => _MessageView(icon: icon, title: title, message: message, action: action, illustration: illustration);
}

class ForbiddenState extends StatelessWidget {
  const ForbiddenState({super.key, this.message});
  final String? message;

  @override
  Widget build(BuildContext context) => _MessageView(
        icon: LucideIcons.shieldAlert,
        iconColor: SsColors.slate,
        illustration: Illustration.accessDenied,
        title: "You don't have access to this",
        message: message ?? 'Ask your institution administrator if you think you should.',
      );
}

/// Error with a Retry action. Distinguishes offline, not-found, permission and server errors.
class ErrorState extends StatelessWidget {
  const ErrorState({super.key, required this.error, this.onRetry});
  final Object error;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    final e = error;
    if (e is ApiException && e.isForbidden && e.code != 'password_change_required') {
      return ForbiddenState(message: e.message);
    }
    final (icon, title, message, art) = switch (e) {
      ApiException(isNetwork: true) => (LucideIcons.wifiOff, "You're offline", e.message, Illustration.offline),
      ApiException(isNotFound: true) => (LucideIcons.searchX, 'Not found', 'It may have been removed, or you no longer have access.', Illustration.notFound),
      ApiException() => (LucideIcons.circleAlert, "Something didn't work", e.message, Illustration.serverError),
      FormatException() => (LucideIcons.circleAlert, "We couldn't read the server's response", 'Please update the app or try again later.', Illustration.serverError),
      _ => (LucideIcons.circleAlert, "Something didn't work", 'Please try again.', Illustration.serverError),
    };
    return _MessageView(
      icon: icon,
      iconColor: SsColors.error,
      illustration: art,
      title: title,
      message: message,
      action: onRetry == null ? null : FilledButton.tonalIcon(onPressed: onRetry, icon: const Icon(LucideIcons.refreshCw, size: 18), label: const Text('Retry')),
    );
  }
}

/// Friendly, user-facing message for any error thrown by an action.
String describeError(Object error) {
  if (error is ApiException) return error.message;
  if (error is FormatException) return "We couldn't read the server's response.";
  return 'Something went wrong. Please try again.';
}

void showSnack(BuildContext context, String message, {bool error = false}) {
  final messenger = ScaffoldMessenger.maybeOf(context);
  messenger?.hideCurrentSnackBar();
  messenger?.showSnackBar(SnackBar(
    content: Row(children: [
      Icon(error ? LucideIcons.circleAlert : LucideIcons.circleCheck, color: SsColors.white, size: 20),
      const SizedBox(width: SsSpace.s3),
      Expanded(child: Text(message)),
    ]),
    backgroundColor: error ? SsColors.error : null,
  ));
}
