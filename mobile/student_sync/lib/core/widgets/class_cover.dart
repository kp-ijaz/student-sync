import 'package:flutter/material.dart';

import '../theme/tokens.dart';

/// Cover photo for a class, chosen from its subject (or title). Classes have no
/// uploaded artwork yet, so each subject area has one bundled photo
/// (assets/images/covers, Pexels licence).
abstract final class ClassCovers {
  // Order matters: the first match wins ("Social Science" is social, not science;
  // "Applied Mathematics" is maths; "Business Studies" is commerce).
  static const _byKeyword = <(List<String>, String)>[
    (['social', 'history', 'geograph', 'politic', 'civics', 'psycholog', 'sociolog'], 'social'),
    (['account', 'business', 'economic', 'commerce', 'finance'], 'commerce'),
    (['math', 'algebra', 'geometry', 'calculus', 'trigonometr', 'statistic', 'quadratic'], 'maths'),
    (['physic', 'mechanic', 'optic', 'electric'], 'physics'),
    (['chemi', 'organic'], 'chemistry'),
    (['hindi', 'malayalam', 'arabic', 'french', 'sanskrit', 'tamil', 'urdu', 'language'], 'languages'),
    (['english', 'ielts', 'literature', 'writing', 'reading', 'grammar', 'essay'], 'english'),
    (['science', 'biology', 'world around us', 'environment', 'evs', 'stem', 'lab'], 'science'),
  ];

  /// The subject decides first; the title (or course name) only when the subject
  /// gives no hint — "Physics" in "Grade 10 Science & Mathematics" is still physics.
  static String assetFor(String? subject, String title) =>
      _match(subject) ?? _match(title) ?? 'assets/images/covers/study.jpg';

  static String? _match(String? text) {
    final t = (text ?? '').toLowerCase();
    if (t.isEmpty) return null;
    for (final (keywords, file) in _byKeyword) {
      if (keywords.any(t.contains)) return 'assets/images/covers/$file.jpg';
    }
    return null;
  }
}

/// A class cover photo with a dark gradient at the bottom so text laid over it stays
/// readable. [child] is placed over the photo.
class ClassCoverImage extends StatelessWidget {
  const ClassCoverImage({super.key, required this.subject, required this.title, this.height, this.radius = SsRadius.card, this.child, this.shade = true});
  final String? subject;
  final String title;
  final double? height;
  final double radius;
  final Widget? child;

  /// Darken the lower part for overlaid text.
  final bool shade;

  @override
  Widget build(BuildContext context) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(radius),
      child: SizedBox(
        height: height,
        child: Stack(fit: StackFit.expand, children: [
          ExcludeSemantics(
            child: Image.asset(
              ClassCovers.assetFor(subject, title),
              fit: BoxFit.cover,
              cacheWidth: 900,
              errorBuilder: (_, _, _) => const DecoratedBox(decoration: BoxDecoration(gradient: SsGradients.brand)),
            ),
          ),
          if (shade)
            const DecoratedBox(
              decoration: BoxDecoration(
                gradient: LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  colors: [Color(0x000B2854), Color(0x330B2854), Color(0xCC0B2854)],
                  stops: [0.3, 0.55, 1],
                ),
              ),
            ),
          ?child,
        ]),
      ),
    );
  }
}
