﻿using Akavache.Http;
using Microsoft.Reactive.Testing;
using ReactiveUI;
using ReactiveUI.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Akavache.Http.Tests
{
    public abstract class HttpSchedulerSharedTests
    {
        protected abstract IHttpScheduler CreateFixture();

        [Fact]
        public void HttpSchedulerShouldCompleteADummyRequest()
        {
            var fixture = CreateFixture();

            fixture.Client = new HttpClient(new TestHttpMessageHandler(_ =>
            {
                var ret = new HttpResponseMessage()
                {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");
                return Observable.Return(ret);
            }));

            fixture.Client.BaseAddress = new Uri("http://example");

            var rq = new HttpRequestMessage(HttpMethod.Get, "/");
            var result = fixture.Schedule(rq, 1)
                .Timeout(TimeSpan.FromSeconds(2.0), RxApp.TaskpoolScheduler)
                .First();

            Console.WriteLine(Encoding.UTF8.GetString(result.Item2));
            Assert.Equal(HttpStatusCode.OK, result.Item1.StatusCode);
            Assert.Equal(3 /*foo*/, result.Item2.Length);
        }

        [Fact]
        public void HttpSchedulerShouldntScheduleLotsOfStuffAtOnce()
        {
            var fixture = CreateFixture();

            var blockedRqs = new Dictionary<HttpRequestMessage, Subject<Unit>>();
            var scheduledCount = default(int);
            var completedCount = default(int);

            fixture.Client = new HttpClient(new TestHttpMessageHandler(rq =>
            {
                scheduledCount++;
                var ret = new HttpResponseMessage()
                {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");

                blockedRqs[rq] = new Subject<Unit>();
                return blockedRqs[rq].Select(_ => ret).Finally(() => completedCount++);
            }));

            fixture.Client.BaseAddress = new Uri("http://example");

            (new TestScheduler()).With(sched =>
            {
                var rqs = Enumerable.Range(0, 5)
                    .Select(x => new HttpRequestMessage(HttpMethod.Get, "/" + x.ToString()))
                    .ToArray();

                var results = rqs.ToObservable()
                    .Select(rq => fixture.Schedule(rq, 10))
                    .Merge()
                    .CreateCollection();

                sched.Start();
                        
                Assert.Equal(4, scheduledCount);
                Assert.Equal(0, completedCount);

                var firstSubj = blockedRqs.First().Value;
                firstSubj.OnNext(Unit.Default); firstSubj.OnCompleted();

                sched.Start();

                Assert.Equal(5, scheduledCount);
                Assert.Equal(1, completedCount);

                foreach (var v in blockedRqs.Values)
                {
                    v.OnNext(Unit.Default); v.OnCompleted();
                }

                sched.Start();

                Assert.Equal(5, scheduledCount);
                Assert.Equal(5, completedCount);
            });
        }
    }

    public class BaseHttpSchedulerSharedTests : HttpSchedulerSharedTests
    {
        protected override IHttpScheduler CreateFixture()
        {
            return new HttpScheduler();
        }
    }

    public class CachingHttpSchedulerSharedTests : HttpSchedulerSharedTests
    {
        protected override IHttpScheduler CreateFixture()
        {
            return new CachingHttpScheduler(new TestBlobCache(), new HttpScheduler());
        }
    }
}