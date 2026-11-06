import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/common.dart';
import '../../core/models/doubt_models.dart';
import '../../core/models/enums.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/illustration.dart';
import '../../core/widgets/notification_bell.dart';
import '../../core/widgets/states.dart';
import 'doubt_card.dart';

/// Doubts that still need something from someone; the rest are answered or closed.
bool isOpenDoubt(DoubtStatus s) => s != DoubtStatus.resolved && s != DoubtStatus.answered && s != DoubtStatus.pendingConfirmation;

/// Doubt Centre (student tab): every doubt on one page — open first, then answered and
/// closed — with "Ask a doubt". Teachers see the same groups inside their Tasks tab.
class DoubtsListScreen extends StatefulWidget {
  const DoubtsListScreen({super.key, required this.canAsk});
  final bool canAsk;

  @override
  State<DoubtsListScreen> createState() => _DoubtsListScreenState();
}

class _DoubtsListScreenState extends State<DoubtsListScreen> {
  int _version = 0;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Doubts'), actions: tabBarActions),
      floatingActionButton: widget.canAsk
          ? Padding(
              // Scaffold places the button from the view padding, which does not include
              // the floating navigation bar the shell adds to the media padding.
              padding: EdgeInsets.only(bottom: (MediaQuery.paddingOf(context).bottom - MediaQuery.viewPaddingOf(context).bottom).clamp(0, double.infinity)),
              child: FloatingActionButton.extended(
                onPressed: () async {
                  final created = await context.push<String>('/doubts/new');
                  if (created != null && mounted) setState(() => _version++);
                },
                icon: const Icon(LucideIcons.messageCirclePlus),
                label: const Text('Ask a doubt'),
              ),
            )
          : null,
      body: AsyncView<PagedResult<DoubtListItem>>(
        key: ValueKey(_version),
        load: () => services.doubts.list(unresolvedOnly: false),
        isEmpty: (p) => p.items.isEmpty,
        empty: (context, _) => EmptyState(
          icon: LucideIcons.messageCircleQuestionMark,
          illustration: Illustration.questions,
          title: 'No doubts yet',
          message: widget.canAsk ? 'Stuck on something? Ask your teacher with "Ask a doubt".' : 'Doubts from your students will appear here.',
        ),
        builder: (context, page, reload) => RefreshIndicator(
          onRefresh: reload,
          child: ListView(
            // Room for the "Ask a doubt" button above the navigation bar.
            padding: pagePadding(context, extraBottom: widget.canAsk ? 64 : 0),
            children: [DoubtGroups(items: page.items, showStudent: !widget.canAsk, onReturn: reload)],
          ),
        ),
      ),
    );
  }
}

/// "Open" then "Answered & closed", for a list of doubts. Not scrollable itself.
class DoubtGroups extends StatelessWidget {
  const DoubtGroups({super.key, required this.items, required this.showStudent, required this.onReturn, this.openTitle = 'Open'});
  final List<DoubtListItem> items;
  final bool showStudent;
  final Future<void> Function() onReturn;
  final String openTitle;

  @override
  Widget build(BuildContext context) {
    final open = items.where((d) => isOpenDoubt(d.status)).toList();
    final done = items.where((d) => !isOpenDoubt(d.status)).toList();
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      SectionHeader(openTitle, icon: LucideIcons.messageCircleQuestionMark, iconColor: SsColors.orangeDeep, count: open.length),
      if (open.isEmpty) const InlineEmpty(icon: LucideIcons.circleCheckBig, text: 'Nothing open right now.'),
      for (final d in open) ...[DoubtCard(doubt: d, showStudent: showStudent, onReturn: onReturn), const SizedBox(height: SsSpace.s3)],
      if (done.isNotEmpty) ...[
        SectionHeader('Answered & closed', icon: LucideIcons.circleCheckBig, iconColor: SsColors.success, count: done.length),
        for (final d in done) ...[DoubtCard(doubt: d, showStudent: showStudent, onReturn: onReturn), const SizedBox(height: SsSpace.s3)],
      ],
    ]);
  }
}
