import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/brand.dart';

class _Slide {
  const _Slide({required this.image, required this.title, required this.body, required this.chipIcon, required this.chip, required this.chipDetail});
  final String image;
  final String title;
  final String body;
  final IconData chipIcon;
  final String chip;
  final String chipDetail;
}

const _slides = [
  _Slide(
    image: 'assets/images/welcome/live.jpg',
    title: 'Every class in one place',
    body: 'Batch, group and one-to-one classes, shown in your own time zone — join with one tap.',
    chipIcon: LucideIcons.video,
    chip: 'Join class',
    chipDetail: 'Today · 19:00',
  ),
  _Slide(
    image: 'assets/images/welcome/anywhere.jpg',
    title: 'Watch, revise, catch up',
    body: 'Class videos unlock around each lesson, with notes and materials right beside them.',
    chipIcon: LucideIcons.circlePlay,
    chip: 'Video unlocked',
    chipDetail: 'Factorising recap',
  ),
  _Slide(
    image: 'assets/images/welcome/grow.jpg',
    title: 'Ask, practise and grow',
    body: 'Send a doubt to your teacher, hand in your work and watch your progress build.',
    chipIcon: LucideIcons.messageCircleQuestionMark,
    chip: 'Doubt answered',
    chipDetail: 'by your teacher',
  ),
];

/// First-run welcome: three slides, then sign in (accounts are issued by the institute office).
class WelcomeScreen extends StatefulWidget {
  const WelcomeScreen({super.key});

  @override
  State<WelcomeScreen> createState() => _WelcomeScreenState();
}

class _WelcomeScreenState extends State<WelcomeScreen> {
  final _pages = PageController();
  int _page = 0;

  @override
  void dispose() {
    _pages.dispose();
    super.dispose();
  }

  Future<void> _finish(String route) async {
    await AppScope.read(context).welcome.complete();
    if (mounted) context.go(route);
  }

  void _next() {
    if (_page == _slides.length - 1) {
      _finish('/login');
    } else {
      _pages.nextPage(duration: SsMotion.medium, curve: SsMotion.curve);
    }
  }

  @override
  Widget build(BuildContext context) {
    final last = _page == _slides.length - 1;
    final reduceMotion = MediaQuery.disableAnimationsOf(context);
    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: SystemUiOverlayStyle.dark,
      child: Scaffold(
        backgroundColor: SsColors.background,
        body: SafeArea(
          child: Column(children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(SsSpace.s5, SsSpace.s3, SsSpace.s3, 0),
              child: Row(children: [
                const BrandLockup(markSize: 34, onDark: false),
                const Spacer(),
                AnimatedOpacity(
                  opacity: last ? 0 : 1,
                  duration: SsMotion.fast,
                  child: TextButton(onPressed: last ? null : () => _finish('/login'), child: const Text('Skip')),
                ),
              ]),
            ),
            Expanded(
              child: PageView.builder(
                controller: _pages,
                itemCount: _slides.length,
                onPageChanged: (i) => setState(() => _page = i),
                itemBuilder: (context, i) => _SlideView(slide: _slides[i]),
              ),
            ),
            // Page indicator: the current page is a longer pill.
            Semantics(
              label: 'Page ${_page + 1} of ${_slides.length}',
              child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                for (var i = 0; i < _slides.length; i++)
                  AnimatedContainer(
                    duration: reduceMotion ? Duration.zero : SsMotion.medium,
                    curve: SsMotion.curve,
                    margin: const EdgeInsets.symmetric(horizontal: 3),
                    width: i == _page ? 28 : 8,
                    height: 8,
                    decoration: BoxDecoration(color: i == _page ? SsColors.blue : SsColors.border, borderRadius: BorderRadius.circular(SsRadius.pill)),
                  ),
              ]),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(SsSpace.s5, SsSpace.s5, SsSpace.s5, SsSpace.s5),
              child: FilledButton(
                key: const Key('welcome.next'),
                style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(56), shape: const StadiumBorder()),
                onPressed: _next,
                child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                  Text(last ? 'Get started' : 'Next'),
                  const SizedBox(width: SsSpace.s2),
                  const Icon(LucideIcons.arrowRight, size: 20),
                ]),
              ),
            ),
          ]),
        ),
      ),
    );
  }
}

class _SlideView extends StatelessWidget {
  const _SlideView({required this.slide});
  final _Slide slide;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    return LayoutBuilder(builder: (context, c) {
      final photoHeight = (c.maxHeight * 0.58).clamp(200.0, 460.0);
      return SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(SsSpace.s5, SsSpace.s4, SsSpace.s5, SsSpace.s4),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          SizedBox(
            height: photoHeight,
            child: Stack(clipBehavior: Clip.none, children: [
              Positioned.fill(
                child: DecoratedBox(
                  decoration: BoxDecoration(borderRadius: BorderRadius.circular(32), boxShadow: SsShadow.floating),
                  child: ClipRRect(
                    borderRadius: BorderRadius.circular(32),
                    child: ExcludeSemantics(child: Image.asset(slide.image, fit: BoxFit.cover, cacheWidth: 1000)),
                  ),
                ),
              ),
              // A floating glass card previews what the feature looks like.
              Positioned(
                left: SsSpace.s4,
                bottom: SsSpace.s4,
                child: ExcludeSemantics(
                  child: Container(
                    padding: const EdgeInsets.fromLTRB(SsSpace.s2, SsSpace.s2, SsSpace.s4, SsSpace.s2),
                    decoration: BoxDecoration(
                      color: SsColors.white.withValues(alpha: 0.92),
                      borderRadius: BorderRadius.circular(SsRadius.pill),
                      boxShadow: SsShadow.floating,
                    ),
                    child: Row(mainAxisSize: MainAxisSize.min, children: [
                      Container(
                        width: 36,
                        height: 36,
                        decoration: const BoxDecoration(gradient: SsGradients.brand, shape: BoxShape.circle),
                        child: Icon(slide.chipIcon, size: 18, color: SsColors.white),
                      ),
                      const SizedBox(width: SsSpace.s2),
                      Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
                        Text(slide.chip, style: const TextStyle(fontFamily: 'Inter', fontSize: 13, fontWeight: FontWeight.w700, color: SsColors.navy)),
                        Text(slide.chipDetail, style: const TextStyle(fontFamily: 'Inter', fontSize: 11, color: SsColors.textSecondary)),
                      ]),
                    ]),
                  ),
                ),
              ),
            ]),
          ),
          const SizedBox(height: SsSpace.s6),
          Semantics(header: true, child: Text(slide.title, style: t.displaySmall)),
          const SizedBox(height: SsSpace.s2),
          Text(slide.body, style: t.bodyLarge?.copyWith(color: SsColors.textSecondary)),
        ]),
      );
    });
  }
}
