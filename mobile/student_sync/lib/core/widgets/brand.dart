import 'package:flutter/material.dart';

import '../theme/tokens.dart';

/// Brand artwork, extracted from the master logo in `docs/brand/` (see its README for
/// where the mark is used alone and where it carries the wordmark).
abstract final class BrandAssets {
  /// The open book with the star — the mark on its own, for light backgrounds.
  static const mark = 'assets/brand/mark.png';

  /// The mark on a white rounded tile, for dark or gradient backgrounds.
  static const markTile = 'assets/brand/mark_tile.png';

  /// The book and the star as separate layers on the mark's canvas (the loader animates the star).
  static const book = 'assets/brand/book.png';
  static const star = 'assets/brand/star.png';

  /// The star alone, trimmed to a square (decoration).
  static const starSolo = 'assets/brand/star_solo.png';

  /// "Student Sync": navy and purple for light backgrounds, white and lilac for dark ones.
  static const wordmark = 'assets/brand/wordmark.png';
  static const wordmarkLight = 'assets/brand/wordmark_light.png';

  /// Width ÷ height of the mark and wordmark artwork.
  static const markAspect = 409 / 340;
  static const wordmarkAspect = 898 / 153;

  /// Centre of the star within the mark canvas, as an [Alignment] (pixel 207, 58 of 409 × 340).
  static const starCentre = Alignment(207 / 409 * 2 - 1, 58 / 340 * 2 - 1);
}

/// The Student Sync mark, [size] tall. Set [onDark] on navy or gradient backgrounds to
/// show it on its white tile so the dark blue page keeps its contrast.
class BrandMark extends StatelessWidget {
  const BrandMark({super.key, this.size = 48, this.heroTag, this.onDark = false});
  final double size;

  /// Set on screens that share the mark so it glides between them (splash → sign in ↔ register).
  final Object? heroTag;
  final bool onDark;

  @override
  Widget build(BuildContext context) {
    final mark = Semantics(
      label: 'Student Sync',
      image: true,
      child: onDark
          ? Image.asset(BrandAssets.markTile, width: size, height: size, filterQuality: FilterQuality.medium)
          : Image.asset(BrandAssets.mark, width: size * BrandAssets.markAspect, height: size, filterQuality: FilterQuality.medium),
    );
    return heroTag == null ? mark : Hero(tag: heroTag!, child: mark);
  }
}

/// The "Student Sync" wordmark, [height] tall.
class BrandWordmark extends StatelessWidget {
  const BrandWordmark({super.key, this.height = 24, this.onDark = false});
  final double height;
  final bool onDark;

  @override
  Widget build(BuildContext context) => Semantics(
        label: 'Student Sync',
        image: true,
        child: Image.asset(
          onDark ? BrandAssets.wordmarkLight : BrandAssets.wordmark,
          width: height * BrandAssets.wordmarkAspect,
          height: height,
          filterQuality: FilterQuality.medium,
        ),
      );
}

/// Loading indicator: the open book stays still while its star turns a quarter and
/// pulses. Shows the still mark when the platform asks for reduced motion.
class BrandSpinner extends StatefulWidget {
  const BrandSpinner({super.key, this.size = 56, this.label = 'Loading'});

  /// Height of the mark.
  final double size;
  final String label;

  @override
  State<BrandSpinner> createState() => _BrandSpinnerState();
}

class _BrandSpinnerState extends State<BrandSpinner> with SingleTickerProviderStateMixin {
  late final AnimationController _spin = AnimationController(vsync: this, duration: const Duration(milliseconds: 1100));
  late final Animation<double> _eased = CurvedAnimation(parent: _spin, curve: Curves.easeInOutCubic);

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (MediaQuery.disableAnimationsOf(context)) {
      _spin.stop();
    } else if (!_spin.isAnimating) {
      _spin.repeat();
    }
  }

  @override
  void dispose() {
    _spin.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final width = widget.size * BrandAssets.markAspect;
    Widget layer(String asset) => Image.asset(asset, width: width, height: widget.size, filterQuality: FilterQuality.medium);
    return Semantics(
      label: widget.label,
      liveRegion: true,
      child: SizedBox(
        width: width,
        height: widget.size,
        child: Stack(children: [
          layer(BrandAssets.book),
          AnimatedBuilder(
            animation: _eased,
            // The star has four points, so a quarter turn looks like a full one.
            builder: (context, child) => Transform.rotate(
              angle: _eased.value * 1.5708,
              alignment: BrandAssets.starCentre,
              child: Transform.scale(scale: 1 + 0.25 * (1 - (2 * _eased.value - 1).abs()), alignment: BrandAssets.starCentre, child: child),
            ),
            child: layer(BrandAssets.star),
          ),
        ]),
      ),
    );
  }
}

/// Mark + wordmark. [onDark] uses the tiled mark and the light wordmark for navy or
/// gradient backgrounds; [center] stacks them, otherwise they sit side by side.
class BrandLockup extends StatelessWidget {
  const BrandLockup({super.key, this.markSize = 56, this.heroTag, this.center = false, this.onDark = true});
  final double markSize;
  final Object? heroTag;
  final bool center;
  final bool onDark;

  @override
  Widget build(BuildContext context) {
    final mark = BrandMark(size: markSize, heroTag: heroTag, onDark: onDark);
    final wordmark = ExcludeSemantics(child: BrandWordmark(height: markSize * 0.42, onDark: onDark));
    return MergeSemantics(
      child: center
          ? Column(mainAxisSize: MainAxisSize.min, children: [mark, const SizedBox(height: SsSpace.s4), wordmark])
          : Row(mainAxisSize: MainAxisSize.min, children: [mark, const SizedBox(width: SsSpace.s3), Flexible(child: FittedBox(fit: BoxFit.scaleDown, child: wordmark))]),
    );
  }
}

/// App-bar title: the mark and the wordmark, for screens that stand for the whole app.
class BrandAppBarTitle extends StatelessWidget {
  const BrandAppBarTitle({super.key});

  @override
  Widget build(BuildContext context) => const BrandLockup(markSize: 28, onDark: false);
}
