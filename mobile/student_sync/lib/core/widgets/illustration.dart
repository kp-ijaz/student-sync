import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

/// unDraw illustrations (assets/illustrations, recoloured to Royal Blue) for empty,
/// error and not-found states. Purely decorative: the state's text carries the meaning.
enum Illustration {
  search('search'),
  notFound('not_found'),
  empty('empty'),
  offline('offline'),
  serverError('server_error'),
  accessDenied('access_denied'),
  allDone('all_done'),
  questions('questions'),
  mailbox('mailbox');

  const Illustration(this.file);
  final String file;

  String get asset => 'assets/illustrations/$file.svg';
}

class IllustrationView extends StatelessWidget {
  const IllustrationView(this.illustration, {super.key, this.height = 180});
  final Illustration illustration;
  final double height;

  @override
  Widget build(BuildContext context) => ExcludeSemantics(
        child: SizedBox(
          height: height,
          child: SvgPicture.asset(illustration.asset, fit: BoxFit.contain, placeholderBuilder: (_) => SizedBox(height: height)),
        ),
      );
}
