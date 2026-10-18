create extension if not exists pg_cron;

select cron.schedule(
    'elemento-abandon-stale-matches',
    '0 * * * *',
    'select public.abandon_stale_matches()'
)
 where not exists (
     select 1
       from cron.job
      where jobname = 'elemento-abandon-stale-matches'
 );
