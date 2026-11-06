import 'package:flutter/material.dart';
import 'package:flutter_markdown_plus/flutter_markdown_plus.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import 'package:url_launcher/url_launcher.dart';

import '../models/common.dart';
import '../theme/status_styles.dart';
import '../theme/tokens.dart';
import 'states.dart';

/// White card: 20 px radius, hairline border + soft shadow (DESIGN.md §5, §9).
class SsCard extends StatelessWidget {
  const SsCard({super.key, required this.child, this.padding = const EdgeInsets.all(SsSpace.s4), this.onTap, this.accent, this.color, this.semanticLabel});

  final Widget child;
  final EdgeInsetsGeometry padding;
  final VoidCallback? onTap;

  /// Optional 4 px left accent bar (e.g. orange for "next class").
  final Color? accent;
  final Color? color;
  final String? semanticLabel;

  @override
  Widget build(BuildContext context) {
    final radius = BorderRadius.circular(SsRadius.card);
    Widget content = Padding(padding: padding, child: child);
    if (accent != null) {
      content = IntrinsicHeight(
        child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Container(width: 4, color: accent),
          Expanded(child: content),
        ]),
      );
    }
    return Semantics(
      container: true,
      button: onTap != null,
      label: semanticLabel,
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: color ?? SsColors.white,
          borderRadius: radius,
          border: Border.all(color: SsColors.hairline),
          boxShadow: SsShadow.card,
        ),
        child: Material(
          type: MaterialType.transparency,
          child: InkWell(
            borderRadius: radius,
            onTap: onTap,
            child: ClipRRect(borderRadius: radius, child: content),
          ),
        ),
      ),
    );
  }
}

/// Navy → blue gradient surface for each role's hero card. Text on it is white.
class HeroCard extends StatelessWidget {
  const HeroCard({super.key, required this.child, this.onTap, this.padding = const EdgeInsets.all(SsSpace.s5), this.semanticLabel});
  final Widget child;
  final VoidCallback? onTap;
  final EdgeInsetsGeometry padding;
  final String? semanticLabel;

  @override
  Widget build(BuildContext context) {
    final radius = BorderRadius.circular(SsRadius.card + 4);
    return Semantics(
      container: true,
      button: onTap != null,
      label: semanticLabel,
      child: DecoratedBox(
        decoration: BoxDecoration(
          gradient: SsGradients.brand,
          borderRadius: radius,
          boxShadow: const [BoxShadow(color: Color(0x290B2854), blurRadius: 28, offset: Offset(0, 12))],
        ),
        child: Material(
          type: MaterialType.transparency,
          child: InkWell(
            borderRadius: radius,
            onTap: onTap,
            child: ClipRRect(
              borderRadius: radius,
              child: Stack(children: [
                // Two soft light discs give the gradient depth without adding noise.
                const Positioned(right: -60, top: -70, child: _Glow(size: 200, alpha: 0x1A)),
                const Positioned(right: 40, bottom: -90, child: _Glow(size: 160, alpha: 0x10)),
                Padding(padding: padding, child: DefaultTextStyle.merge(style: const TextStyle(color: SsColors.white), child: child)),
              ]),
            ),
          ),
        ),
      ),
    );
  }
}

class _Glow extends StatelessWidget {
  const _Glow({required this.size, required this.alpha});
  final double size;
  final int alpha;

  @override
  Widget build(BuildContext context) => IgnorePointer(
        child: Container(
          width: size,
          height: size,
          decoration: BoxDecoration(shape: BoxShape.circle, color: Color.fromARGB(alpha, 255, 255, 255)),
        ),
      );
}

/// An icon on a tinted rounded square, used to lead rows and tiles.
class IconTile extends StatelessWidget {
  const IconTile(this.icon, {super.key, this.color = SsColors.blue, this.size = 40, this.background});
  final IconData icon;
  final Color color;
  final double size;
  final Color? background;

  @override
  Widget build(BuildContext context) => ExcludeSemantics(
        child: Container(
          width: size,
          height: size,
          decoration: BoxDecoration(color: background ?? color.withValues(alpha: 0.10), borderRadius: BorderRadius.circular(size * 0.32)),
          child: Icon(icon, size: size * 0.5, color: color),
        ),
      );
}

/// Text + colour status pill with a leading dot. Never relies on colour alone.
class StatusBadge extends StatelessWidget {
  const StatusBadge(this.style, {super.key, this.dense = false});
  final StatusStyle style;
  final bool dense;

  @override
  Widget build(BuildContext context) {
    final fg = badgeTextColor(style.color);
    return Semantics(
      label: 'Status: ${style.label}',
      excludeSemantics: true,
      child: Container(
        padding: EdgeInsets.symmetric(horizontal: dense ? SsSpace.s2 : 10, vertical: dense ? 3 : 5),
        decoration: BoxDecoration(
          color: style.color.withValues(alpha: 0.12),
          borderRadius: BorderRadius.circular(SsRadius.pill),
        ),
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          Container(width: 6, height: 6, decoration: BoxDecoration(color: style.color, shape: BoxShape.circle)),
          const SizedBox(width: 6),
          Text(
            style.label,
            style: TextStyle(fontFamily: 'Inter', fontSize: 12, height: 16 / 12, fontWeight: FontWeight.w600, color: fg),
          ),
        ]),
      ),
    );
  }
}

/// Neutral label pill (e.g. teaching mode).
class TagPill extends StatelessWidget {
  const TagPill(this.label, {super.key, this.icon});
  final String label;
  final IconData? icon;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
        decoration: BoxDecoration(color: SsColors.fill, borderRadius: BorderRadius.circular(SsRadius.pill)),
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          if (icon != null) ...[ExcludeSemantics(child: Icon(icon, size: 13, color: SsColors.navy)), const SizedBox(width: 5)],
          Flexible(
            child: Text(label,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontFamily: 'Inter', fontSize: 12, height: 16 / 12, fontWeight: FontWeight.w600, color: SsColors.navy)),
          ),
        ]),
      );
}

class SectionHeader extends StatelessWidget {
  const SectionHeader(this.title, {super.key, this.trailing, this.icon, this.iconColor = SsColors.blue, this.count});
  final String title;
  final Widget? trailing;

  /// Optional tinted icon before the title.
  final IconData? icon;
  final Color iconColor;

  /// Optional count shown as a small pill after the title.
  final int? count;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: SsSpace.s5, bottom: SsSpace.s3),
        child: Row(children: [
          if (icon != null) ...[IconTile(icon!, color: iconColor, size: 32), const SizedBox(width: 10)],
          // The title takes all the room it needs; the count sits right after it.
          Expanded(
            child: Row(children: [
              Flexible(
                child: Semantics(
                  header: true,
                  child: Text(title, style: Theme.of(context).textTheme.titleMedium?.copyWith(fontSize: 17)),
                ),
              ),
              if (count != null) ...[
                const SizedBox(width: SsSpace.s2),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: SsSpace.s2, vertical: 2),
                  decoration: BoxDecoration(color: SsColors.navy, borderRadius: BorderRadius.circular(SsRadius.pill)),
                  child: Text('$count', style: const TextStyle(fontFamily: 'Inter', fontSize: 12, fontWeight: FontWeight.w700, color: SsColors.white)),
                ),
              ],
            ]),
          ),
          ?trailing,
        ]),
      );
}

/// Fully rounded segmented control: a navy pill slides under the selected option.
/// Labels never wrap (they shrink); icons only show when [showIcons] is true.
class SsSegmented<T> extends StatelessWidget {
  const SsSegmented({super.key, required this.segments, required this.selected, required this.onChanged, this.showIcons = false});
  final List<({T value, String label, IconData? icon})> segments;
  final T selected;
  final ValueChanged<T> onChanged;
  final bool showIcons;

  @override
  Widget build(BuildContext context) {
    final index = segments.indexWhere((s) => s.value == selected).clamp(0, segments.length - 1);
    final n = segments.length;
    final duration = MediaQuery.disableAnimationsOf(context) ? Duration.zero : SsMotion.medium;
    return Container(
      height: 48,
      padding: const EdgeInsets.all(SsSpace.s1),
      decoration: BoxDecoration(
        color: SsColors.white,
        borderRadius: BorderRadius.circular(SsRadius.pill),
        border: Border.all(color: SsColors.hairline),
        boxShadow: SsShadow.card,
      ),
      child: Stack(children: [
        AnimatedAlign(
          duration: duration,
          curve: SsMotion.curve,
          alignment: Alignment(n == 1 ? 0 : -1 + 2 * index / (n - 1), 0),
          child: FractionallySizedBox(
            widthFactor: 1 / n,
            heightFactor: 1,
            child: DecoratedBox(
              decoration: BoxDecoration(
                gradient: SsGradients.brand,
                borderRadius: BorderRadius.circular(SsRadius.pill),
                boxShadow: const [BoxShadow(color: Color(0x330B2854), blurRadius: 10, offset: Offset(0, 3))],
              ),
            ),
          ),
        ),
        Row(children: [
          for (var i = 0; i < n; i++)
            Expanded(
              child: Semantics(
                button: true,
                selected: i == index,
                child: InkWell(
                  customBorder: const StadiumBorder(),
                  onTap: () => onChanged(segments[i].value),
                  child: Center(
                    child: Padding(
                      padding: const EdgeInsets.symmetric(horizontal: SsSpace.s2),
                      child: Row(mainAxisSize: MainAxisSize.min, children: [
                        if (showIcons && segments[i].icon != null) ...[
                          Icon(segments[i].icon, size: 16, color: i == index ? SsColors.white : SsColors.slate),
                          const SizedBox(width: 6),
                        ],
                        Flexible(
                          child: FittedBox(
                            fit: BoxFit.scaleDown,
                            child: AnimatedDefaultTextStyle(
                              duration: duration,
                              style: TextStyle(
                                fontFamily: 'Inter',
                                fontSize: 14,
                                fontWeight: FontWeight.w600,
                                color: i == index ? SsColors.white : SsColors.textBody,
                              ),
                              child: Text(segments[i].label, maxLines: 1, softWrap: false),
                            ),
                          ),
                        ),
                      ]),
                    ),
                  ),
                ),
              ),
            ),
        ]),
      ]),
    );
  }
}

/// "See all →" link for a [SectionHeader].
class SeeAllButton extends StatelessWidget {
  const SeeAllButton({super.key, required this.label, required this.onPressed});
  final String label;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) => TextButton(
        onPressed: onPressed,
        style: TextButton.styleFrom(padding: const EdgeInsets.symmetric(horizontal: SsSpace.s2), textStyle: const TextStyle(fontFamily: 'Inter', fontSize: 14, fontWeight: FontWeight.w600)),
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          Text(label),
          const SizedBox(width: 2),
          const Icon(LucideIcons.chevronRight, size: 16),
        ]),
      );
}

class StatCard extends StatelessWidget {
  const StatCard({super.key, required this.label, required this.value, this.hint});
  final String label;
  final String value;
  final String? hint;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    return SsCard(
      semanticLabel: '$label: $value${hint == null ? '' : '. $hint'}',
      child: ExcludeSemantics(
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(value, style: t.displaySmall?.copyWith(fontSize: 26, fontFeatures: const [FontFeature.tabularFigures()])),
          const SizedBox(height: 2),
          Text(label, style: t.labelMedium?.copyWith(color: SsColors.textBody, fontWeight: FontWeight.w600)),
          if (hint != null) ...[const SizedBox(height: 2), Text(hint!, style: t.labelSmall)],
        ]),
      ),
    );
  }
}

/// Grid of stat cards that reflows with width and text scale.
class StatGrid extends StatelessWidget {
  const StatGrid({super.key, required this.children});
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => LayoutBuilder(builder: (context, c) {
        final columns = c.maxWidth >= 560 ? 4 : 2;
        final width = (c.maxWidth - SsSpace.s3 * (columns - 1)) / columns;
        return Wrap(
          spacing: SsSpace.s3,
          runSpacing: SsSpace.s3,
          children: [for (final child in children) SizedBox(width: width, child: child)],
        );
      });
}

/// A progress metric with its value and an expandable "How is this calculated?".
class MetricTile extends StatelessWidget {
  const MetricTile(this.metric, {super.key});
  final Metric metric;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final pct = metric.percent;
    final color = pct == null ? SsColors.blue : (pct >= 75 ? SsColors.success : pct >= 50 ? SsColors.blue : SsColors.warning);
    return SsCard(
      padding: EdgeInsets.zero,
      child: Theme(
        data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
        child: ExpansionTile(
          tilePadding: const EdgeInsets.symmetric(horizontal: SsSpace.s4, vertical: SsSpace.s1),
          childrenPadding: const EdgeInsets.fromLTRB(SsSpace.s4, 0, SsSpace.s4, SsSpace.s4),
          expandedCrossAxisAlignment: CrossAxisAlignment.start,
          title: Text(metric.label, style: t.titleSmall),
          subtitle: pct == null
              ? null
              : Padding(
                  padding: const EdgeInsets.only(top: SsSpace.s2),
                  child: ClipRRect(
                    borderRadius: BorderRadius.circular(SsRadius.pill),
                    child: LinearProgressIndicator(
                      value: (pct / 100).clamp(0, 1),
                      minHeight: 8,
                      color: color,
                      backgroundColor: SsColors.fill,
                      semanticsLabel: metric.label,
                    ),
                  ),
                ),
          trailing: Text(metric.value, style: t.titleMedium?.copyWith(fontFeatures: const [FontFeature.tabularFigures()])),
          children: [
            Text('How is this calculated?', style: t.labelMedium?.copyWith(color: SsColors.navy, fontWeight: FontWeight.w600)),
            const SizedBox(height: SsSpace.s1),
            Text(metric.howCalculated, style: t.bodySmall),
          ],
        ),
      ),
    );
  }
}

class InfoRow extends StatelessWidget {
  const InfoRow({super.key, required this.icon, required this.label, required this.value, this.trailing});
  final IconData icon;
  final String label;
  final String value;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(children: [
        IconTile(icon, size: 38),
        const SizedBox(width: SsSpace.s3),
        Expanded(
          child: Semantics(
            label: '$label: $value',
            excludeSemantics: true,
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(label, style: t.labelSmall),
              const SizedBox(height: 1),
              Text(value, style: t.bodyMedium?.copyWith(fontWeight: FontWeight.w500, color: SsColors.textPrimary)),
            ]),
          ),
        ),
        ?trailing,
      ]),
    );
  }
}

/// Inset grouped list (settings style): rows in one card separated by inset dividers.
class SsListGroup extends StatelessWidget {
  const SsListGroup({super.key, required this.children});
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => SsCard(
        padding: const EdgeInsets.symmetric(vertical: SsSpace.s1),
        child: Column(children: [
          for (var i = 0; i < children.length; i++) ...[
            if (i > 0) const Divider(indent: 68, endIndent: SsSpace.s4, color: SsColors.fill),
            children[i],
          ],
        ]),
      );
}

/// A row for [SsListGroup]: tinted icon, title, optional subtitle and trailing widget.
class SsListRow extends StatelessWidget {
  const SsListRow({super.key, required this.icon, required this.title, this.subtitle, this.trailing, this.onTap, this.color = SsColors.blue, this.showChevron});
  final IconData icon;
  final String title;
  final String? subtitle;
  final Widget? trailing;
  final VoidCallback? onTap;
  final Color color;

  /// Defaults to showing a chevron when the row is tappable and has no trailing widget.
  final bool? showChevron;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final chevron = showChevron ?? (onTap != null && trailing == null);
    return MergeSemantics(
      child: InkWell(
        onTap: onTap,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 60),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: SsSpace.s4, vertical: SsSpace.s2),
            child: Row(children: [
              IconTile(icon, color: color, size: 38),
              const SizedBox(width: 14),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
                  Text(title, style: t.titleSmall),
                  if (subtitle != null) Text(subtitle!, style: t.bodySmall, maxLines: 2, overflow: TextOverflow.ellipsis),
                ]),
              ),
              ?trailing,
              if (chevron) const ExcludeSemantics(child: Icon(LucideIcons.chevronRight, size: 18, color: SsColors.slate)),
            ]),
          ),
        ),
      ),
    );
  }
}

/// One home shortcut: a tinted icon and a short label.
class ShortcutTile {
  const ShortcutTile({required this.icon, required this.title, required this.onTap, this.color = SsColors.blue});
  final IconData icon;
  final String title;
  final VoidCallback onTap;
  final Color color;
}

/// Four home shortcuts as a compact 2×2 grid of pill buttons (chip-sized).
class ShortcutGrid extends StatelessWidget {
  const ShortcutGrid({super.key, required this.tiles}) : assert(tiles.length == 4, 'The home grid holds four shortcuts.');
  final List<ShortcutTile> tiles;

  @override
  Widget build(BuildContext context) => Column(children: [
        for (var row = 0; row < 2; row++) ...[
          if (row > 0) const SizedBox(height: SsSpace.s2),
          Row(children: [
            Expanded(child: _ShortcutPill(tile: tiles[row * 2])),
            const SizedBox(width: SsSpace.s2),
            Expanded(child: _ShortcutPill(tile: tiles[row * 2 + 1])),
          ]),
        ],
      ]);
}

class _ShortcutPill extends StatelessWidget {
  const _ShortcutPill({required this.tile});
  final ShortcutTile tile;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        label: tile.title,
        excludeSemantics: true,
        child: Material(
          color: SsColors.white,
          shape: const StadiumBorder(side: BorderSide(color: SsColors.hairline)),
          child: InkWell(
            customBorder: const StadiumBorder(),
            onTap: tile.onTap,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(SsSpace.s2, SsSpace.s2, SsSpace.s3, SsSpace.s2),
              child: Row(children: [
                IconTile(tile.icon, color: tile.color, size: 32),
                const SizedBox(width: SsSpace.s2),
                Expanded(
                  child: Text(
                    tile.title,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontFamily: 'Inter', fontSize: 14, fontWeight: FontWeight.w600, color: SsColors.textPrimary),
                  ),
                ),
              ]),
            ),
          ),
        ),
      );
}

/// A quiet "nothing here" line inside a section.
class InlineEmpty extends StatelessWidget {
  const InlineEmpty({super.key, required this.icon, required this.text});
  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(SsSpace.s4),
        decoration: BoxDecoration(
          color: SsColors.white.withValues(alpha: 0.6),
          borderRadius: BorderRadius.circular(SsRadius.card),
          border: Border.all(color: SsColors.border),
        ),
        child: Row(children: [
          ExcludeSemantics(child: Icon(icon, size: 20, color: SsColors.slate)),
          const SizedBox(width: SsSpace.s3),
          Expanded(child: Text(text, style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: SsColors.textSecondary))),
        ]),
      );
}

/// Fully rounded pill tab bar on a white track. Uses [controller], or the enclosing
/// [DefaultTabController].
class SsTabBar extends StatelessWidget implements PreferredSizeWidget {
  const SsTabBar({super.key, required this.tabs, this.controller, this.padding = const EdgeInsets.fromLTRB(SsSpace.s4, SsSpace.s1, SsSpace.s4, SsSpace.s3)});
  final List<Widget> tabs;
  final TabController? controller;
  final EdgeInsets padding;

  @override
  Size get preferredSize => Size.fromHeight(48 + padding.vertical);

  @override
  Widget build(BuildContext context) => Padding(
        padding: padding,
        child: Container(
          height: 48,
          padding: const EdgeInsets.all(SsSpace.s1),
          decoration: BoxDecoration(
            color: SsColors.white,
            borderRadius: BorderRadius.circular(SsRadius.pill),
            border: Border.all(color: SsColors.hairline),
            boxShadow: SsShadow.card,
          ),
          child: TabBar(
            controller: controller,
            tabAlignment: TabAlignment.fill,
            labelPadding: const EdgeInsets.symmetric(horizontal: SsSpace.s2),
            indicator: BoxDecoration(
              gradient: SsGradients.brand,
              borderRadius: BorderRadius.circular(SsRadius.pill),
              boxShadow: const [BoxShadow(color: Color(0x330B2854), blurRadius: 10, offset: Offset(0, 3))],
            ),
            tabs: [for (final tab in tabs) Tab(height: 40, child: FittedBox(fit: BoxFit.scaleDown, child: tab))],
          ),
        ),
      );
}

/// Renders teacher-authored Markdown. Links open externally; images are not loaded
/// from arbitrary hosts (notes are rendered as text-first content).
class MarkdownView extends StatelessWidget {
  const MarkdownView(this.data, {super.key, this.selectable = true});
  final String data;
  final bool selectable;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final sheet = MarkdownStyleSheet.fromTheme(theme).copyWith(
      p: theme.textTheme.bodyMedium,
      h1: theme.textTheme.headlineSmall,
      h2: theme.textTheme.titleLarge?.copyWith(fontSize: 18),
      h3: theme.textTheme.titleMedium,
      code: const TextStyle(fontFamily: 'monospace', fontSize: 13, backgroundColor: SsColors.blue50),
      blockquoteDecoration: BoxDecoration(color: SsColors.blue50, borderRadius: BorderRadius.circular(SsRadius.control)),
      tableBorder: TableBorder.all(color: SsColors.border),
      tableCellsPadding: const EdgeInsets.all(SsSpace.s2),
    );
    return MarkdownBody(
      data: data,
      selectable: selectable,
      styleSheet: sheet,
      shrinkWrap: true,
      imageBuilder: (uri, title, alt) => Text('[Image: ${alt ?? uri}]', style: theme.textTheme.bodySmall),
      onTapLink: (text, href, title) {
        if (href != null) openExternal(context, href);
      },
    );
  }
}

/// Opens [url] outside the app (browser / meeting app). Reports failures instead of
/// silently doing nothing.
Future<void> openExternal(BuildContext context, String url, {bool inApp = false}) async {
  final uri = Uri.tryParse(url);
  if (uri == null || !(uri.isScheme('https') || uri.isScheme('http'))) {
    showSnack(context, 'This link is not a valid web address.', error: true);
    return;
  }
  bool ok;
  try {
    ok = await launchUrl(uri, mode: inApp ? LaunchMode.inAppBrowserView : LaunchMode.externalApplication);
  } on Exception {
    ok = false;
  }
  if (!ok && context.mounted) showSnack(context, "Couldn't open the link on this device.", error: true);
}

/// Page padding. The bottom clears system gestures and, inside a tab, the floating
/// navigation bar (which the shell reports through the bottom media padding).
EdgeInsets pagePadding(BuildContext context, {double extraBottom = 0}) =>
    EdgeInsets.fromLTRB(SsSpace.s4, SsSpace.s2, SsSpace.s4, SsSpace.s6 + extraBottom + MediaQuery.paddingOf(context).bottom);

/// Bottom action bar background that extends under the home indicator.
class BottomBarSafeArea extends StatelessWidget {
  const BottomBarSafeArea({super.key, this.top = false, required this.child});
  final bool top;
  final Widget child;

  @override
  Widget build(BuildContext context) => DecoratedBox(
        decoration: const BoxDecoration(color: SsColors.white, border: Border(top: BorderSide(color: SsColors.hairline))),
        child: SafeArea(top: top, child: child),
      );
}
