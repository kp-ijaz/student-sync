import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/theme/tokens.dart';
import '../../core/widgets/brand.dart';

/// Shared hero tag so the brand mark glides between the auth screens.
const brandHeroTag = 'student-sync-brand-mark';

/// Layout for the signed-out screens. Phones: a navy→blue brand hero with the form in
/// a card that overlaps it. Tablets and landscape (≥ 840 px): a brand panel with what
/// the app offers on the left and the form on the right.
class AuthShell extends StatelessWidget {
  const AuthShell({super.key, required this.child, this.onBack, this.compactHero = false});
  final Widget child;

  /// Shows a back button on the hero.
  final VoidCallback? onBack;

  /// A shorter hero, for long forms.
  final bool compactHero;

  @override
  Widget build(BuildContext context) {
    // Light status-bar icons over the dark hero.
    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: SystemUiOverlayStyle.light,
      child: Scaffold(
        backgroundColor: SsColors.background,
        body: LayoutBuilder(
          builder: (context, c) {
            if (c.maxWidth >= 840) {
              return Row(
                children: [
                  Expanded(flex: 5, child: _BrandPanel(onBack: onBack)),
                  Expanded(
                    flex: 6,
                    child: SafeArea(
                      left: false,
                      child: Center(
                        child: SingleChildScrollView(
                          padding: const EdgeInsets.all(SsSpace.s6),
                          child: ConstrainedBox(constraints: const BoxConstraints(maxWidth: 440), child: child),
                        ),
                      ),
                    ),
                  ),
                ],
              );
            }
            final top = MediaQuery.paddingOf(context).top;
            // The gradient sits behind the hero and the top of the card; below that the card
            // rests on the soft background. (No Transform offsets, so taps land where drawn.)
            return SingleChildScrollView(
              child: DecoratedBox(
                decoration: const BoxDecoration(gradient: _heroGradient),
                child: Column(
                  children: [
                    _Hero(topPadding: top, onBack: onBack, compact: compactHero),
                    Stack(
                      children: [
                        const Positioned(
                          left: 0,
                          right: 0,
                          top: SsSpace.s7,
                          bottom: 0,
                          child: ColoredBox(color: SsColors.background),
                        ),
                        Padding(
                          padding: const EdgeInsets.fromLTRB(SsSpace.s4, 0, SsSpace.s4, SsSpace.s6),
                          child: Center(
                            child: ConstrainedBox(
                              constraints: const BoxConstraints(maxWidth: 480),
                              child: DecoratedBox(
                                decoration: BoxDecoration(
                                  color: SsColors.white,
                                  borderRadius: BorderRadius.circular(SsRadius.sheet),
                                  boxShadow: const [
                                    BoxShadow(color: Color(0x140B2854), blurRadius: 24, offset: Offset(0, 8)),
                                    BoxShadow(color: Color(0x0F0B2854), blurRadius: 4, offset: Offset(0, 1)),
                                  ],
                                ),
                                child: Padding(padding: const EdgeInsets.fromLTRB(SsSpace.s5, SsSpace.s5, SsSpace.s5, SsSpace.s4), child: child),
                              ),
                            ),
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            );
          },
        ),
      ),
    );
  }
}

const _heroGradient = SsGradients.brand;

class _Hero extends StatelessWidget {
  const _Hero({required this.topPadding, required this.onBack, required this.compact});
  final double topPadding;
  final VoidCallback? onBack;
  final bool compact;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: double.infinity,
      child: Stack(
        children: [
          const Positioned(right: -60, top: -40, child: _StarDecoration(size: 220)),
          Padding(
            padding: EdgeInsets.fromLTRB(SsSpace.s5, topPadding + (onBack == null ? SsSpace.s6 : SsSpace.s2), SsSpace.s5, SsSpace.s6),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (onBack != null)
                  Padding(
                    padding: const EdgeInsets.only(bottom: SsSpace.s2),
                    child: IconButton(
                      tooltip: 'Back to sign in',
                      onPressed: onBack,
                      style: IconButton.styleFrom(foregroundColor: SsColors.white, backgroundColor: const Color(0x26FFFFFF)),
                      icon: const Icon(LucideIcons.arrowLeft),
                    ),
                  ),
                BrandLockup(markSize: compact ? 48 : 60, heroTag: brandHeroTag),
                if (!compact) ...[
                  const SizedBox(height: SsSpace.s5),
                  const Text(
                    'Your classes, videos and teachers — all in one place.',
                    style: TextStyle(fontFamily: 'Inter', fontSize: 15, height: 22 / 15, color: Color(0xE6FFFFFF)),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// The logo's star, large and faint, behind the hero text (purely decorative).
class _StarDecoration extends StatelessWidget {
  const _StarDecoration({required this.size});
  final double size;

  @override
  Widget build(BuildContext context) => ExcludeSemantics(
    child: IgnorePointer(
      child: Opacity(opacity: 0.16, child: Image.asset(BrandAssets.starSolo, width: size, height: size)),
    ),
  );
}

class _BrandPanel extends StatelessWidget {
  const _BrandPanel({required this.onBack});
  final VoidCallback? onBack;

  static const _features = [
    (LucideIcons.calendarDays, 'Your class calendar', 'Every live class, in your own time zone.'),
    (LucideIcons.monitorPlay, 'Pre-recorded class videos', 'They unlock automatically before or after each class.'),
    (LucideIcons.messageCircleQuestionMark, 'Ask your teacher', 'Send a question about any class and get an answer.'),
  ];

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: const BoxDecoration(gradient: _heroGradient),
      child: Stack(
        children: [
          const Positioned(right: -90, bottom: -90, child: _StarDecoration(size: 380)),
          SafeArea(
            right: false,
            child: Padding(
              padding: const EdgeInsets.all(SsSpace.s7),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  if (onBack != null)
                    IconButton(
                      tooltip: 'Back to sign in',
                      onPressed: onBack,
                      style: IconButton.styleFrom(foregroundColor: SsColors.white, backgroundColor: const Color(0x26FFFFFF)),
                      icon: const Icon(LucideIcons.arrowLeft),
                    ),
                  const Spacer(),
                  const BrandLockup(markSize: 72, heroTag: brandHeroTag),
                  const SizedBox(height: SsSpace.s7),
                  for (final (icon, title, body) in _features)
                    Padding(
                      padding: const EdgeInsets.only(bottom: SsSpace.s5),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Container(
                            width: 40,
                            height: 40,
                            decoration: BoxDecoration(color: const Color(0x1FFFFFFF), borderRadius: BorderRadius.circular(SsRadius.control)),
                            child: Icon(icon, color: SsColors.orange, size: 22),
                          ),
                          const SizedBox(width: SsSpace.s4),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  title,
                                  style: const TextStyle(fontFamily: 'Inter', fontWeight: FontWeight.w600, fontSize: 16, color: SsColors.white),
                                ),
                                const SizedBox(height: 2),
                                Text(
                                  body,
                                  style: const TextStyle(fontFamily: 'Inter', fontSize: 14, height: 20 / 14, color: Color(0xD9FFFFFF)),
                                ),
                              ],
                            ),
                          ),
                        ],
                      ),
                    ),
                  const Spacer(),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Coloured message strip used on the auth screens (errors, session expired, notices).
class AuthBanner extends StatelessWidget {
  const AuthBanner({super.key, required this.message, required this.color, this.icon = LucideIcons.info});
  final String message;
  final Color color;
  final IconData icon;

  @override
  Widget build(BuildContext context) => Semantics(
    liveRegion: true,
    child: Container(
      padding: const EdgeInsets.all(SsSpace.s3),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(SsRadius.control),
        border: Border.all(color: color.withValues(alpha: 0.4)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, color: color, size: 20),
          const SizedBox(width: SsSpace.s2),
          Expanded(child: Text(message, style: Theme.of(context).textTheme.bodyMedium)),
        ],
      ),
    ),
  );
}

/// "──── text ────" separator.
class LabeledDivider extends StatelessWidget {
  const LabeledDivider(this.label, {super.key});
  final String label;

  @override
  Widget build(BuildContext context) => Row(
    children: [
      const Expanded(child: Divider()),
      Padding(
        padding: const EdgeInsets.symmetric(horizontal: SsSpace.s3),
        child: Text(label, style: Theme.of(context).textTheme.bodySmall?.copyWith(color: SsColors.textBody)),
      ),
      const Expanded(child: Divider()),
    ],
  );
}
